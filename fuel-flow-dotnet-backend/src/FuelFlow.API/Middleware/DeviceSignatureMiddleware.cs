using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Infrastructure.Caching;
using FuelFlow.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Middleware;

public sealed class DeviceSignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DeviceSignatureMiddleware> _logger;

    public DeviceSignatureMiddleware(
        RequestDelegate next,
        ILogger<DeviceSignatureMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            context.Request.Path,
            deviceAuthOptions.RequireSignatureForEndpoints);

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

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Device signature required" });
            return;
        }

        if (!long.TryParse(timestamp, out var timestampMs))
        {
            _logger.LogWarning("Invalid timestamp format: {Timestamp}", timestamp);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid timestamp" });
            return;
        }

        var requestTime = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);
        var now = DateTimeOffset.UtcNow;
        var timeDiff = Math.Abs((now - requestTime).TotalMilliseconds);

        if (timeDiff > deviceAuthOptions.TimestampToleranceMs)
        {
            _logger.LogWarning(
                "Request timestamp outside tolerance window: {TimeDiff}ms",
                timeDiff);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Request expired" });
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

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Request already processed" });
            return;
        }

        var device = await dbContext.Devices
            .FirstOrDefaultAsync(d => d.DeviceId == deviceId && d.Status == DeviceStatus.Active);

        if (device == null)
        {
            _logger.LogWarning("Device {DeviceId} not found or revoked", deviceId);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Device not registered or revoked" });
            return;
        }

        context.Request.EnableBuffering();
        var bodyReader = new StreamReader(context.Request.Body);
        var body = await bodyReader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var payload = $"{context.Request.Method}{context.Request.Path}{body}{timestamp}";

        var isValid = VerifySignature(payload, signature, device.PublicKey);

        if (!isValid)
        {
            _logger.LogWarning(
                "Invalid signature for device {DeviceId}",
                deviceId);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid signature" });
            return;
        }

        await cacheService.SetAsync(
            nonce,
            "1",
            TimeSpan.FromSeconds(deviceAuthOptions.SignatureNonceTtlSeconds));

        device.LastSeenAt = DateTime.UtcNow;
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

    private bool ShouldRequireSignature(PathString path, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.TrimEnd('*');
                if (path.StartsWithSegments(prefix))
                    return true;
            }
            else
            {
                if (path.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private bool VerifySignature(string payload, string signatureBase64, string publicKeyPem)
    {
        try
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            return rsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying signature: {Message}", ex.Message);
            return false;
        }
    }
}
