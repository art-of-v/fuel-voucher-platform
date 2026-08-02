using FuelFlow.Middleware;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.Extensions;

internal static class PipelineSetup
{
    internal static WebApplication UseAppPipeline(this WebApplication app)
    {
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseExceptionHandler();
        app.UseCors();
        app.UseResponseCaching();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<SessionValidationMiddleware>();
        app.UseAuthorization();
        app.UseMiddleware<DeviceSignatureMiddleware>();
        app.MapControllers();

        app.MapGet("/health", async (ApplicationDbContext db) =>
        {
            try
            {
                _ = await db.Database.GetPendingMigrationsAsync();
                return Results.Ok(new { status = "healthy", database = "connected" });
            }
            catch
            {
                return Results.Problem("Database unreachable", statusCode: 503);
            }
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
