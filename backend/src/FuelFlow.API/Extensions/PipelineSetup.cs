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
        // The request-body ceiling. After CORS, so a refusal carries the headers a browser
        // needs to read the status; before the rate limiter, so an oversized body is refused
        // before it costs anything.
        app.UseMiddleware<RequestSizeLimitMiddleware>();
        // The update floor. Before the response cache, so a cached 200 for a public endpoint
        // cannot be replayed to a stale client instead of the 426 that tells it to update - the
        // public reads are exactly the ones cached for 300s. Before authentication, so the
        // refusal is not pre-empted by a 401 the client reads as a wrong password (planning #728).
        app.UseMiddleware<AppVersionFloorMiddleware>();
        app.UseResponseCaching();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<SessionValidationMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<DeviceSignatureMiddleware>();
        // After DeviceSignatureMiddleware: both middlewares need the same buffered body, and the
        // device id the signature middleware validated is the id the attestation key is bound to.
        app.UseMiddleware<AttestationMiddleware>();
        // After DeviceSignatureMiddleware: that middleware can add the device's identity, and the
        // scope must be resolved from the principal the request actually ends up with.
        app.UseMiddleware<TenantScopeMiddleware>();
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
        //
        // The body answers the one question a probe asks - is this instance up - and names nothing
        // else. Which dependency is down is an operator's question, and this endpoint is anonymous
        // because an uptime monitor has to reach it without a credential: the anonymous reachability
        // is the reviewed decision, the dependency names beside it never were. The reason goes to
        // the log with the trace id instead, so the operator still gets the answer the monitor
        // cannot carry. /health/ready remains for a dependency-shaped check.
        app.MapMethods("/health", [HttpMethods.Get, HttpMethods.Head], async (
            ApplicationDbContext db,
            IConnectionMultiplexer redis,
            HttpContext http,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("HealthProbe");
            var healthy = true;

            try
            {
                // The probe asks "is the database reachable?", and it used to ask it the expensive
                // way: GetPendingMigrationsAsync() reads the WHOLE __EFMigrationsHistory table
                // (183 rows when this was written) and diffs it against the model snapshot - and then the result
                // was thrown away, so a pending migration never made the probe unhealthy anyway.
                // SELECT 1 is the same question with one cheap round trip.
                await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Health probe: the database is unreachable");
                healthy = false;
            }

            try
            {
                await redis.GetDatabase().PingAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Health probe: Redis is unreachable");
                healthy = false;
            }

            return healthy
                ? Results.Ok(new { status = "healthy" })
                // The one error envelope, like every other failure: the status in `title`, the
                // sentence in `detail`, the correlation id beside them - and the correlation id is
                // the whole point, because it is what leads from a monitor's page to the log line
                // that says which dependency failed.
                : Results.Problem(
                    "One or more dependencies unreachable",
                    instance: http.Request.Path.Value,
                    statusCode: 503,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = http.TraceIdentifier
                    });
        }).AllowAnonymous();

        app.MapGet("/api/app-version", (IConfiguration config) =>
        {
            var section = config.GetSection(AppVersionOptions.SectionName);
            return Results.Ok(new
            {
                minimumVersion = section["MinimumVersion"] ?? AppVersionOptions.DefaultMinimumVersion,
                iosStoreUrl = section["IosStoreUrl"] ?? "",
                androidStoreUrl = section["AndroidStoreUrl"] ?? ""
            });
        }).AllowAnonymous();

        return app;
    }
}
