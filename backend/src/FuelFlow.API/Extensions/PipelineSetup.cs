using FuelFlow.Middleware;

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

        return app;
    }
}
