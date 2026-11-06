using System.Text;
using FuelFlow.Features.Attestation;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Security.Attestation;
using Microsoft.Extensions.Options;

namespace FuelFlow.Middleware;

/// <summary>
/// Verifies the app-attestation proof on the endpoints that spend money or authenticate a device.
/// </summary>
/// <remarks>
/// <para>
/// The default is ReportOnly: a proof that is present is checked and the result is logged, but a
/// missing or bad proof does not break the request. That is deliberate. The oldest supported
/// mobile build does not have App Attest or Play Integrity at all, and turning enforcement on in
/// the same deploy would lock every existing install out of checkout. ReportOnly measures the
/// fleet first; Enforce is turned on per platform, for builds at or above a minimum version, once
/// the real-device evidence exists.
/// </para>
/// <para>
/// Measuring is a count, not a log grep: every verdict on a protected endpoint is recorded in
/// <c>fuelflow_attestation_verdict_requests_total</c>, labelled by the server's own reason code and by
/// whether the request would be held to the proof under Enforce. The last label is what makes the
/// decision readable before it is made - those two labels together are the population that flipping
/// the mode would start refusing.
/// </para>
/// <para>
/// The rollout headers (<c>X-Client-OS</c>, <c>X-App-Version</c>) decide whether this request is
/// from a build new enough to be held to the rule. They are trivially spoofable, and they are not
/// the security control - the cryptographic proof is. They only keep the rollout from breaking
/// old clients.
/// </para>
/// </remarks>
public sealed class AttestationMiddleware
{
    public const string ProviderHeader = "X-Attestation-Provider";
    public const string ChallengeHeader = "X-Attestation-Challenge";
    public const string ProofHeader = "X-Attestation-Proof";
    public const string KeyIdHeader = "X-Attestation-Key-Id";
    public const string ClientOsHeader = "X-Client-OS";
    public const string AppVersionHeader = "X-App-Version";
    public const string DeviceIdHeader = "X-Device-Id";
    public const string ClientPlatformHeader = "X-Client-Platform";

    private readonly RequestDelegate _next;
    private readonly ILogger<AttestationMiddleware> _logger;
    private readonly FuelFlowMetrics _metrics;

    public AttestationMiddleware(
        RequestDelegate next,
        ILogger<AttestationMiddleware> logger,
        FuelFlowMetrics metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task InvokeAsync(
        HttpContext context,
        AttestationService attestation,
        IOptionsMonitor<AttestationOptions> options,
        IWebHostEnvironment environment)
    {
        var settings = options.CurrentValue;

        if (!settings.Enabled || settings.Mode == AttestationMode.Off)
        {
            await _next(context);
            return;
        }

        if (!RequestPathMatcher.IsProtected(context.Request.Path, settings.ProtectedEndpoints))
        {
            await _next(context);
            return;
        }

        var enforceEligible = IsEnforceEligible(context, settings);

        // One predicate, two consequences. In ReportOnly it only labels the metric; in Enforce it
        // decides who is refused. Keeping them the same function is what makes the report-only
        // measurement mean the enforcement, rather than something near it.
        var enforce = settings.Mode == AttestationMode.Enforce && enforceEligible;

        if (enforce && environment.IsDevelopment() && settings.AllowDevelopmentBypass)
        {
            _logger.LogWarning(
                "Attestation would be enforced for {Path}, but the development bypass is active",
                context.Request.Path);
            enforce = false;
        }

        var provider = Header(context, ProviderHeader);
        var challenge = Header(context, ChallengeHeader);
        var proof = Header(context, ProofHeader);
        var keyId = Header(context, KeyIdHeader);
        var deviceId = Header(context, DeviceIdHeader);
        var clientOs = Header(context, ClientOsHeader);
        var appVersion = Header(context, AppVersionHeader);

        if (string.IsNullOrWhiteSpace(challenge)
            && string.IsNullOrWhiteSpace(proof)
            && string.IsNullOrWhiteSpace(provider))
        {
            _metrics.AttestationVerdict(Verdict(AttestationStatus.Missing), "ATTESTATION_REQUIRED", enforceEligible);

            if (enforce)
            {
                _logger.LogWarning(
                    "Attestation proof missing for {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

                // Lets the native client tell an attestation refusal apart from an expired
                // access token, so it neither refreshes nor clears the session for this.
                context.Response.Headers["X-Attestation-Error"] = "ATTESTATION_REQUIRED";

                await ApiErrorResponse.WriteAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "This endpoint requires a valid app-attestation proof. Update the app and try again.",
                    "ATTESTATION_REQUIRED",
                    cancellationToken: context.RequestAborted);

                return;
            }

            // ReportOnly exists to measure the fleet before enforcement. A missing proof is
            // the most important measurement (an old build), so it is recorded rather than
            // passed over silently.
            _logger.LogInformation(
                "Attestation Missing for {Method} {Path} from device {DevicePrefix} os {ClientOs} version {AppVersion}; report-only, request continues",
                context.Request.Method,
                context.Request.Path,
                Short(deviceId),
                clientOs,
                appVersion);

            await _next(context);
            return;
        }

        AttestationCheckResult result;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            result = AttestationCheckResult.Invalid(
                "ATTESTATION_DEVICE_REQUIRED",
                "An attested request must carry a device id.");
        }
        else
        {
            var body = await ReadBodyAsync(context);

            try
            {
                result = await attestation.VerifyAsync(
                    context.Request.Method,
                    context.Request.Path.Value ?? string.Empty,
                    body,
                    deviceId,
                    clientOs,
                    new AttestationProof(provider ?? string.Empty, challenge ?? string.Empty, proof ?? string.Empty, keyId),
                    context.RequestAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Attestation verification failed unexpectedly for {Method} {Path}",
                    context.Request.Method,
                    context.Request.Path);

                result = AttestationCheckResult.Unavailable(
                    "The attestation verifier is unavailable.");
            }
        }

        if (result.IsValid)
        {
            _metrics.AttestationVerdict(Verdict(result.Status), "none", enforceEligible);

            if (!enforce)
            {
                _logger.LogInformation(
                    "Attestation Valid for {Method} {Path} from device {DevicePrefix} os {ClientOs} version {AppVersion} via {Provider}; report-only",
                    context.Request.Method,
                    context.Request.Path,
                    Short(deviceId),
                    clientOs,
                    appVersion,
                    provider);
            }

            await _next(context);
            return;
        }

        // Recorded once for both outcomes: the same bad proof is a measurement in report-only and a
        // refusal in enforce, and the count must not depend on which one the box is configured for.
        _metrics.AttestationVerdict(Verdict(result.Status), result.Code ?? "none", enforceEligible);

        if (!enforce)
        {
            _logger.LogWarning(
                "Attestation {Status} ({Code}) for {Method} {Path} from device {DevicePrefix} os {ClientOs} version {AppVersion}; report-only, request continues",
                result.Status,
                result.Code,
                context.Request.Method,
                context.Request.Path,
                Short(deviceId),
                clientOs,
                appVersion);

            await _next(context);
            return;
        }

        var status = result.Status == AttestationStatus.Unavailable
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status401Unauthorized;

        _logger.LogWarning(
            "Attestation {Status} ({Code}) for {Method} {Path} from device {DevicePrefix} os {ClientOs} version {AppVersion}; rejected",
            result.Status,
            result.Code,
            context.Request.Method,
            context.Request.Path,
            Short(deviceId),
            clientOs,
            appVersion);

        context.Response.Headers["X-Attestation-Error"] = result.Code ?? "ATTESTATION_INVALID";

        await ApiErrorResponse.WriteAsync(
            context,
            status,
            result.Detail ?? "The app-attestation proof was rejected.",
            result.Code,
            cancellationToken: context.RequestAborted);
    }

    /// <summary>
    /// Whether this request comes from the population the enforcement gate applies to: a mobile
    /// client, an OS named in <see cref="AttestationOptions.EnforcePlatforms"/>, and a build at or
    /// above <see cref="AttestationOptions.MinimumEnforcedAppVersion"/>.
    /// </summary>
    /// <remarks>
    /// The mode is deliberately NOT consulted here. The same question is asked under ReportOnly, where
    /// it labels the metric, and under Enforce, where it decides whether the request is refused — so
    /// "what would enforcement deny" is computed by the predicate that will do the denying, not by a
    /// second copy of it that can drift. The headers are spoofable; that is acceptable because this
    /// labels a rollout measurement, while the cryptographic verdict beside it is the control.
    /// </remarks>
    private static bool IsEnforceEligible(HttpContext context, AttestationOptions settings)
    {
        var platform = Header(context, ClientPlatformHeader);

        if (!string.Equals(platform, "mobile", StringComparison.OrdinalIgnoreCase))
            return false;

        var clientOs = Header(context, ClientOsHeader);

        if (string.IsNullOrWhiteSpace(clientOs)
            || !settings.EnforcePlatforms.Contains(clientOs, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var appVersion = Header(context, AppVersionHeader);

        return AppVersionOptions.Compare(appVersion, settings.MinimumEnforcedAppVersion) >= 0;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        if (!context.Request.Body.CanRead)
            return string.Empty;

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync(context.RequestAborted);

        context.Request.Body.Position = 0;

        return body;
    }

    private static string? Header(HttpContext context, string name) =>
        context.Request.Headers[name].FirstOrDefault();

    private static string Verdict(AttestationStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string Short(string? value) =>
        string.IsNullOrEmpty(value) ? "-" : value.Length <= 8 ? value : value[..8];
}
