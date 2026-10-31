using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Middleware;

/// <summary>
/// Tells an outdated native client to update, in a shape it can act on: 426 with
/// <c>APP_UPDATE_REQUIRED</c>, instead of a 401 that reads like a wrong password.
/// </summary>
/// <remarks>
/// <para>
/// The primary instrument stays the floor the app polls (<c>/api/app-version</c>, see
/// <see cref="AppVersionOptions.MinimumVersion"/>): the client compares itself at launch and shows
/// the update screen. This middleware is the backstop for the requests that leave before that
/// comparison finishes - and for a client that cannot make it at all. On 2026-10-21 a staff sign-in
/// on an outdated build ended as <c>401 IDX14100: JWT is not well formed</c>, which is
/// indistinguishable from a broken password to the person trying to get in (planning #728).
/// </para>
/// <para>
/// It refuses only when it is sure: the client must say it is native and must state a version that
/// parses. A request with neither header is a browser, or a build older than the headers
/// themselves - guessing that such a client is out of date would lock out one that works.
/// </para>
/// <para>
/// Three paths stay reachable by design. <c>/api/app-version</c> is how the client learns the floor
/// and is what draws the update screen; refusing it would leave the client unable to find out it
/// is out of date. <c>/api/updates</c> is the OTA channel - the way a fleet that cannot visit a
/// store recovers without one, which is exactly how #728 was closed. And the probes do not come
/// from the app at all. Refusing any of them would turn a nudge into a brick.
/// </para>
/// </remarks>
public sealed class AppVersionFloorMiddleware
{
    /// <summary>The machine code on the refusal, beside the 426.</summary>
    public const string ErrorCode = "APP_UPDATE_REQUIRED";

    private static readonly string[] ExemptPaths =
    [
        "/api/app-version",
        "/api/updates",
        "/health",
        "/metrics"
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<AppVersionFloorMiddleware> _logger;

    public AppVersionFloorMiddleware(
        RequestDelegate next,
        ILogger<AppVersionFloorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<AppVersionOptions> options)
    {
        var minimumVersion = options.CurrentValue.MinimumVersion;

        if (!IsBelowFloor(context, minimumVersion))
        {
            await _next(context);
            return;
        }

        var appVersion = Header(context, AttestationMiddleware.AppVersionHeader);

        // A warning, not information: an operator reading the log needs to see a fleet that is
        // still asking with a build older than the floor. The volume is bounded by the update
        // screen - once it renders, the client stops calling the API except for the exempt paths.
        _logger.LogWarning(
            "Refused {Method} {Path} for app version {AppVersion}: below the {MinimumVersion} floor",
            context.Request.Method,
            context.Request.Path,
            appVersion,
            minimumVersion);

        await ApiErrorResponse.WriteAsync(
            context,
            StatusCodes.Status426UpgradeRequired,
            $"This version of the app is no longer supported. Update the app to {minimumVersion} or newer and try again.",
            ErrorCode,
            new Dictionary<string, object?> { ["minimumVersion"] = minimumVersion },
            context.RequestAborted);
    }

    private static bool IsBelowFloor(HttpContext context, string? minimumVersion)
    {
        if (IsExempt(context.Request.Path))
            return false;

        var platform = Header(context, AttestationMiddleware.ClientPlatformHeader);

        if (!string.Equals(platform, "mobile", StringComparison.OrdinalIgnoreCase))
            return false;

        var appVersion = Header(context, AttestationMiddleware.AppVersionHeader);

        return AppVersionOptions.Compare(appVersion, minimumVersion) < 0;
    }

    private static bool IsExempt(PathString path)
    {
        var value = path.Value ?? string.Empty;

        return ExemptPaths.Any(exempt =>
            value.Equals(exempt, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith(exempt + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Header(HttpContext context, string name) =>
        context.Request.Headers[name].FirstOrDefault();
}
