using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FuelFlow.API.Extensions;

internal static class RateLimiterSetup
{
    internal const string SendCodePolicy = "send-code";
    internal const string VerifyCodePolicy = "verify-code";
    internal const string DeviceChallengePolicy = "device-challenge";
    internal const string DeviceVerifyPolicy = "device-verify";
    internal const string PurchasePolicy = "purchase";
    internal const string ReferralWritePolicy = "referral-write";

    internal static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SendCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            options.AddPolicy(VerifyCodePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetIp(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
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
        });

        return services;
    }

    private static string GetIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string GetUserId(HttpContext context)
    {
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return userId;

        return GetIp(context);
    }
}
