namespace FuelFlow.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = DateTime.UtcNow;
        var path = context.Request.Path;

        await _next(context);

        if (path.StartsWithSegments("/api"))
        {
            var duration = (DateTime.UtcNow - start).TotalMilliseconds;
            var method = context.Request.Method;
            var statusCode = context.Response.StatusCode;
            
            _logger.LogInformation("{Method} {Path} {StatusCode} in {Duration}ms", 
                method, path, statusCode, duration);
        }
    }
}
