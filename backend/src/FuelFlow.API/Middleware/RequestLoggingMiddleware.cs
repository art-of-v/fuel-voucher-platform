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
            "--> {RequestId} {Method} {Scheme}://{Host}{Path} from {RemoteIp} origin={Origin} ctype={ContentType} len={ContentLength} ua={UserAgent}",
            requestId,
            request.Method,
            request.Scheme,
            request.Host,
            request.Path,
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
            // Log the stable user id (GUID), never the phone number or first name.
            // Those are PII and this line ships to Loki + on-box logs (see
            // docs/OBSERVABILITY.md). The id is correlatable but not personal, and
            // matches the ClaimTypes.NameIdentifier ?? "sub" convention used by the
            // controllers to read the current user.
            user = context.User.Identity?.IsAuthenticated == true
                ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? context.User.FindFirst("sub")?.Value
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
