using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace FuelFlow.Middleware;

public sealed class DeviceSignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DeviceSignatureMiddleware> _logger;
    private readonly DeviceSignatureVerifier _signatureVerifier;

    public DeviceSignatureMiddleware(
        RequestDelegate next,
        ILogger<DeviceSignatureMiddleware> logger,
        DeviceSignatureVerifier signatureVerifier)
    {
        _next = next;
        _logger = logger;
        _signatureVerifier = signatureVerifier;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        ICacheService cacheService,
        IOptions<DeviceAuthOptions> options,
        IWebHostEnvironment environment)
    {
        var deviceAuthOptions = options.Value;

        if (!deviceAuthOptions.Enabled)
        {
            if (environment.IsDevelopment() && deviceAuthOptions.AllowDevelopmentBypass)
            {
                _logger.LogDebug(
                    "Device auth disabled - development bypass active for {Path}",
                    context.Request.Path);
            }

            await _next(context);
            return;
        }

        var requiresSignature = ShouldRequireSignature(
            context.Request.Method,
            context.Request.Path,
            deviceAuthOptions,
            context.Request.Headers[AttestationMiddleware.AppVersionHeader].FirstOrDefault());

        if (!requiresSignature)
        {
            await _next(context);
            return;
        }

        if (environment.IsDevelopment() && deviceAuthOptions.AllowDevelopmentBypass)
        {
            _logger.LogWarning(
                "Device signature required for {Path} but development bypass is active - allowing request",
                context.Request.Path);

            await _next(context);
            return;
        }

        var deviceId = context.Request.Headers["X-Device-Id"].FirstOrDefault();
        var signature = context.Request.Headers["X-Signature"].FirstOrDefault();
        var timestamp = context.Request.Headers["X-Timestamp"].FirstOrDefault();

        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp))
        {
            _logger.LogWarning(
                "Missing device signature headers for {Path}",
                context.Request.Path);

            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Device signature required");
            return;
        }

        if (!long.TryParse(timestamp, out var timestampMs))
        {
            _logger.LogWarning("Invalid timestamp format: {Timestamp}", timestamp);
            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Invalid timestamp");
            return;
        }

        var requestTime = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
        var now = SystemClock.Now;
        var timeDiff = Math.Abs((now - requestTime).TotalMilliseconds);

        if (timeDiff > deviceAuthOptions.TimestampToleranceMs)
        {
            _logger.LogWarning(
                "Request timestamp outside tolerance window: {TimeDiff}ms",
                timeDiff);

            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Request expired");
            return;
        }
        var nonce = $"nonce:{deviceId}:{timestamp}";
        var nonceExists = await cacheService.ExistsAsync(nonce);

        if (nonceExists)
        {
            _logger.LogWarning(
                "Replay attack detected for device {DeviceId} with timestamp {Timestamp}",
                deviceId,
                timestamp);

            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Request already processed");
            return;
        }

        var device = await dbContext.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.Status == DeviceStatus.Active);

        if (device == null)
        {
            _logger.LogWarning("Device {DeviceId} not found or revoked", deviceId);
            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Device not registered or revoked");
            return;
        }

        // The signature proves possession of the device key, but nothing yet
        // ties that device to the JWT identity presented in this request.
        // Without this check a request could carry user B's JWT while signing
        // with user A's device (confused deputy). Require the device to belong
        // to the authenticated principal.
        var jwtUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(jwtUserId)
            || !Guid.TryParse(jwtUserId, out var jwtUserIdParsed)
            || jwtUserIdParsed != device.UserId)
        {
            _logger.LogWarning(
                "Device {DeviceId} is not bound to the authenticated user; rejecting",
                deviceId);
            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Device is not bound to this user");
            return;
        }

        context.Request.EnableBuffering();
        var bodyReader = new StreamReader(context.Request.Body);
        var body = await bodyReader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var verification = _signatureVerifier.VerifyRequest(
            device,
            context.Request.Method,
            context.Request.Path,
            body,
            timestamp,
            signature);

        if (!verification.IsValid)
        {
            _logger.LogWarning(
                "Invalid signature for device {DeviceId} (tried {Algorithms} over payload {PayloadVersions})",
                deviceId,
                string.Join(",", verification.Attempts.Select(a => a.Algorithm)),
                string.Join(",", verification.TriedPayloadVersions.Select(v => $"v{v}")));

            await ApiErrorResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Invalid signature");
            return;
        }

        // A device enrolled before the pins existed carries no scheme; the signature just proved
        // possession, so record the one that verified and the fallback disappears for good. The
        // LastSeenAt update below shares this SaveChanges, so pinning costs no extra round trip.
        if (device.SignatureAlgorithm is null && verification.MatchedAlgorithm is { } matched)
        {
            device.SignatureAlgorithm = matched;
            _logger.LogInformation(
                "Pinned legacy device {DeviceId} to {Algorithm}",
                deviceId,
                matched);
        }

        // The payload version is pinned the same way, but a stale pin is not a failed signature:
        // a bundle can change the shape it signs while the row keeps the old version, and by then
        // the sign-in that would refresh the row is already blocked by this very check. Recording
        // the shape that verified is what closes that loop - it costs nothing here, because the
        // SaveChanges below was happening anyway.
        if (verification.MatchedPayloadVersion is { } matchedVersion
            && matchedVersion != device.SignaturePayloadVersion)
        {
            _logger.LogInformation(
                "Re-pinned device {DeviceId} from payload v{OldVersion} to v{NewVersion}",
                deviceId,
                device.SignaturePayloadVersion,
                matchedVersion);

            device.SignaturePayloadVersion = matchedVersion;
        }

        // Nonce TTL must outlast the timestamp tolerance, or a consumed signed
        // request becomes replayable once its nonce is evicted from cache but its
        // timestamp is still within the accepted window.
        //
        // Twice the tolerance, not once, and the factor is load-bearing. The timestamp
        // check is SYMMETRIC - |now - T| <= tolerance - so a device whose clock runs up to a
        // full tolerance FAST is accepted, which anchors the signed timestamp as late as
        // (arrival + tolerance). The nonce is written on ARRIVAL, and the timestamp stays
        // acceptable until (T + tolerance), i.e. up to (2 * tolerance) after the nonce was
        // written. With a TTL of only `tolerance`, a fast-clocked device therefore had a real
        // replay window of up to `tolerance` seconds on /api/purchases{,/bulk,/renew}: the note
        // came down while the ticket was still valid. Ordering the nonce expiry at exactly
        // (T + tolerance) for the worst-case anchor is what closes that window, and 2 * tolerance
        // is the smallest value that does so for every accepted timestamp.
        //
        // `SignatureNonceTtlSeconds` stays as an operator floor: a configured value LOWER than
        // the computed one cannot re-open the window, so it only ever raises it.
        var toleranceSeconds = deviceAuthOptions.TimestampToleranceMs / 1000;
        var nonceTtlSeconds = Math.Max(
            deviceAuthOptions.SignatureNonceTtlSeconds,
            toleranceSeconds * 2);
        await cacheService.SetAsync(
            nonce,
            "1",
            TimeSpan.FromSeconds(nonceTtlSeconds));

        device.LastSeenAt = SystemClock.UtcNow;
        dbContext.Devices.Update(device);
        await dbContext.SaveChangesAsync();

        var claims = new List<Claim>
        {
            new Claim("DeviceId", deviceId),
            new Claim(ClaimTypes.NameIdentifier, device.UserId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "DeviceAuth");
        context.User.AddIdentity(identity);

        _logger.LogInformation(
            "Device signature verified for {DeviceId}, user {UserId}",
            deviceId,
            device.UserId);

        await _next(context);
    }

    /// <summary>
    /// Decides whether this request has to carry a device signature: either its route is in the
    /// unconditional list, or it is in the version-gated one and the caller's app version has reached
    /// the version that signs it.
    /// <para>
    /// The two lists exist because a signed set can only grow on the server once the client signs.
    /// A route moved into the unconditional list while shipped bundles do not sign it answers 401 to
    /// every phone that has not updated, so the new routes are gated by app version instead and only
    /// start being enforced once the build that signs them is what the stores serve.
    /// </para>
    /// <para>
    /// The path comparison itself lives in <see cref="RequestPathMatcher"/>, which is the same
    /// normalisation-and-wildcard rule the attestation middleware uses - one boundary, one
    /// implementation. Over-matching is deliberately safe: a path variant that routing would not
    /// have dispatched answers 401 instead of 404, whereas under-matching silently drops the device
    /// binding from a money endpoint.
    /// </para>
    /// </summary>
    private static bool ShouldRequireSignature(
        string method,
        PathString path,
        DeviceAuthOptions options,
        string? appVersion)
    {
        if (RequestPathMatcher.IsProtected(path, options.RequireSignatureForEndpoints))
            return true;

        if (options.RequireSignatureForEndpointsFrom.Count == 0)
            return false;

        var enforced = new List<string>(options.RequireSignatureForEndpointsFrom.Count);

        foreach (var (pattern, minimumVersion) in options.RequireSignatureForEndpointsFrom)
        {
            if (!PatternMatchesMethod(pattern, method))
                continue;

            // Compare returns null for anything unparseable, which must not enforce: an app without
            // the header predates the signed route, and refusing it here would lock it out.
            if (AppVersionOptions.Compare(appVersion, minimumVersion) is not >= 0)
                continue;

            enforced.Add(StripMethod(pattern));
        }

        return enforced.Count > 0 && RequestPathMatcher.IsProtected(path, enforced);
    }

    /// <summary>
    /// A pattern may start with a verb (<c>"DELETE /api/purchases/*"</c>) when only that method needs
    /// the proof; without one it applies to every method.
    /// </summary>
    private static bool PatternMatchesMethod(string pattern, string method)
    {
        var separator = pattern.IndexOf(' ');

        return separator < 0
            || pattern.AsSpan(0, separator).Equals(method, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripMethod(string pattern)
    {
        var separator = pattern.IndexOf(' ');

        return separator < 0 ? pattern : pattern[(separator + 1)..];
    }
}
