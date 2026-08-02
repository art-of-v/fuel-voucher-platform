using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace FuelFlow.Middleware;

/// <summary>
/// Logs every incoming HTTP request — method, path, origin, content type, client
/// IP, response status and duration — at the outermost layer of the pipeline so
/// that even CORS-rejected, preflight (OPTIONS) and client-aborted requests are
/// captured in the logs.
/// </summary>
internal sealed class RequestLoggingMiddleware
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
        var request = context.Request;
        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "--> {RequestId} {Method} {Scheme}://{Host}{Path}{QueryString} from {RemoteIp} origin={Origin} ctype={ContentType} len={ContentLength} ua={UserAgent}",
            requestId,
            request.Method,
            request.Scheme,
            request.Host,
            request.Path,
            request.QueryString,
            context.Connection.RemoteIpAddress,
            request.Headers.Origin.ToString(),
            request.ContentType,
            request.ContentLength,
            request.Headers.UserAgent.ToString());

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "<-- {RequestId} {Method} {Path} FAILED after {ElapsedMs}ms with exception",
                requestId, request.Method, request.Path, sw.ElapsedMilliseconds);
            throw;
        }

        sw.Stop();

        string? user = null;
        try
        {
            user = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("first_name")?.Value
                  ?? context.User.FindFirst(ClaimTypes.Name)?.Value
                  ?? context.User.Identity.Name
                : null;
        }
        catch
        {
            // Ignore: user lookup is best-effort.
        }

        var status = context.Response.StatusCode;
        if (status >= 500)
        {
            _logger.LogError(
                "<-- {RequestId} {Method} {Path} responded {Status} after {ElapsedMs}ms user={User}",
                requestId, request.Method, request.Path, status, sw.ElapsedMilliseconds, user);
        }
        else if (status >= 400)
        {
            _logger.LogWarning(
                "<-- {RequestId} {Method} {Path} responded {Status} after {ElapsedMs}ms user={User}",
                requestId, request.Method, request.Path, status, sw.ElapsedMilliseconds, user);
        }
        else
        {
            _logger.LogInformation(
                "<-- {RequestId} {Method} {Path} responded {Status} after {ElapsedMs}ms user={User}",
                requestId, request.Method, request.Path, status, sw.ElapsedMilliseconds, user);
        }
    }
}
