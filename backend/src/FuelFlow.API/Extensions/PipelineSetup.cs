using FuelFlow.Middleware;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.API.Extensions;

internal static class PipelineSetup
{
    internal static WebApplication UseAppPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<DeviceSignatureMiddleware>();
        app.MapControllers();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

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
