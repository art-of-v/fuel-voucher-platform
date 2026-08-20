using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Extensions;

internal static class RateLimiterSetup
{
    internal const string SendCodePolicy = "send-code";
    internal const string VerifyCodePolicy = "verify-code";
    internal const string DeviceChallengePolicy = "device-challenge";
    internal const string DeviceVerifyPolicy = "device-verify";
    internal const string PurchasePolicy = "purchase";
    internal const string ReferralWritePolicy = "referral-write";
    internal const string RefreshPolicy = "refresh";

    /// <summary>
    /// Trusts X-Forwarded-For/X-Forwarded-Proto from the platform load
    /// balancer so rate limits are keyed by the real client IP. Without
    /// this, every request looks like it comes from Render's proxy and all
    /// users share one rate-limit bucket (and attackers can spoof the raw
    /// header that GetIp used to read manually).
    /// </summary>
    internal static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Render's load balancer uses dynamic egress IPs, so known
            // proxies cannot be enumerated. The API only receives traffic
            // through the LB, so trust the immediate connection and take
            // only the last hop (ForwardLimit defaults to 1).
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        return services;
    }

    internal static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SendCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPhoneOrIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = IsDevBypass(context) ? int.MaxValue : 3,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(VerifyCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetPhoneOrIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = IsDevBypass(context) ? int.MaxValue : 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DeviceChallengePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DeviceVerifyPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(5),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(PurchasePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetUserId(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(ReferralWritePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetUserId(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(RefreshPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private static bool IsDevBypass(HttpContext context)
    {
        var authOptions = context.RequestServices.GetService<IOptions<AuthOptions>>();
        return authOptions?.Value.DevBypass ?? false;
    }

    private static string GetIp(HttpContext context)
    {
        // UseForwardedHeaders rewrites RemoteIpAddress from the trusted
        // X-Forwarded-For header. The raw header must NOT be read here:
        // clients can spoof it to escape IP-based rate limits.
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Keys the auth rate limit on the normalized phone number so rotating IPs
    /// cannot SMS-bomb a victim. Falls back to the client IP when the request
    /// body does not carry a parseable phoneNumber.
    /// </summary>
    private static string GetPhoneOrIp(HttpContext context)
    {
        var phone = TryGetPhone(context);
        return string.IsNullOrWhiteSpace(phone) ? GetIp(context) : phone;
    }

    private static string? TryGetPhone(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var body = reader.ReadToEnd();
            context.Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
                return null;

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("phoneNumber", out var prop) ||
                prop.ValueKind != JsonValueKind.String)
                return null;

            var raw = prop.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var phoneService = context.RequestServices.GetService<IPhoneNumberService>();
            try
            {
                return phoneService?.Normalize(raw) ?? raw;
            }
            catch
            {
                return raw;
            }
        }
        catch
        {
            return null;
        }
    }

    private static string GetUserId(HttpContext context)
    {
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return userId;

        return GetIp(context);
    }
}
