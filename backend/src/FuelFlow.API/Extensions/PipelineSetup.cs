using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
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

        var observability = app.Configuration
            .GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        if (observability.Prometheus.Enabled)
        {
            // Opt out of the global RequireAuthenticatedUser FallbackPolicy (AuthSetup).
            // Prometheus scrapes unauthenticated, so without this every scrape is a silent
            // 401 and the target sits permanently down. Caddy blocks /metrics on the public
            // edge (see deploy/Caddyfile), so only internal Prometheus can reach this.
            app.MapPrometheusScrapingEndpoint().AllowAnonymous();
        }

        // Liveness: process is responsive. Intentionally does no I/O so a slow or
        // briefly-unavailable database cannot cause the container to be killed.
        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
            .AllowAnonymous();

        // Readiness: dependencies reachable. Used by the load balancer to decide
        // whether this instance should receive traffic.
        app.MapHealthChecks("/health/ready").AllowAnonymous();

        // Combined liveness + readiness for backwards compatibility with existing
        // probes (UptimeRobot). Probes both DB and Redis.
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
