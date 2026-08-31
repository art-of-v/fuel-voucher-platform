using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace FuelFlow.API.Extensions;

internal static class PipelineSetup
{
    internal static WebApplication UseAppPipeline(this WebApplication app)
    {
        // Must run first so downstream middleware (logging, rate limiting,
        // auth) sees the real client IP and scheme behind the LB.
        app.UseForwardedHeaders();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseCors();
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<SessionValidationMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<DeviceSignatureMiddleware>();
        app.MapControllers();

        app.MapMethods("/health", [HttpMethods.Get, HttpMethods.Head], async (ApplicationDbContext db, IConnectionMultiplexer redis) =>
        {
            var dbStatus = "connected";
            var redisStatus = "connected";
            var healthy = true;

            try
            {
                _ = await db.Database.GetPendingMigrationsAsync();
            }
            catch
            {
                dbStatus = "unreachable";
                healthy = false;
            }

            try
            {
                await redis.GetDatabase().PingAsync();
            }
            catch
            {
                redisStatus = "unreachable";
                healthy = false;
            }

            return healthy
                ? Results.Ok(new { status = "healthy", database = dbStatus, redis = redisStatus })
                : Results.Problem(
                    "One or more dependencies unreachable",
                    statusCode: 503,
                    extensions: new Dictionary<string, object?>
                    {
                        ["database"] = dbStatus,
                        ["redis"] = redisStatus
                    });
        }).AllowAnonymous();

        app.MapGet("/api/app-version", (IConfiguration config) =>
        {
            var section = config.GetSection(AppVersionOptions.SectionName);
            return Results.Ok(new
            {
                minimumVersion = section["MinimumVersion"] ?? "1.0.0",
                iosStoreUrl = section["IosStoreUrl"] ?? "",
                androidStoreUrl = section["AndroidStoreUrl"] ?? ""
            });
        }).AllowAnonymous();

        return app;
    }
}
