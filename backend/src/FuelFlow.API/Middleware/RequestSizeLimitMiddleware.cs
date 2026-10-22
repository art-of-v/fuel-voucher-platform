using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Middleware;

/// <summary>
/// Refuses a request whose body is larger than the ceiling that applies to its route.
/// </summary>
/// <remarks>
/// <para>
/// The ceiling is a property of the API, not of the web server, so it is enforced here rather
/// than only in the Kestrel configuration: Kestrel refuses an oversized body before it is
/// buffered, but a limit that lives only in the server configuration is a limit that a test
/// host does not have and a future host may not have either. The middleware applies the same
/// number to both, so the ceiling holds wherever the API runs.
/// </para>
/// <para>
/// It runs after the CORS middleware, so a refusal carries the CORS headers a browser needs to
/// read the status instead of reporting an opaque network error, and before the rate limiter,
/// so an oversized body is refused before it costs anything.
/// </para>
/// </remarks>
internal sealed class RequestSizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestSizeLimitMiddleware> _logger;

    public RequestSizeLimitMiddleware(RequestDelegate next, ILogger<RequestSizeLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var limit = RequestSizeLimits.For(context);

        // The same number for the server: Kestrel refuses a body over the limit before it is
        // buffered, and a route that raises the ceiling for itself raises it for the server too.
        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is not null)
        {
            sizeFeature.MaxRequestBodySize = limit;
        }

        // A body that declares its length can be refused before a byte of it is read.
        if (context.Request.ContentLength > limit)
        {
            await RefuseAsync(context, limit);
            return;
        }

        // A body that does not declare its length (chunked) can only be refused while it is
        // read, so the stream itself counts.
        context.Request.Body = new SizeLimitedReadStream(context.Request.Body, limit);

        try
        {
            await _next(context);
        }
        catch (RequestBodyTooLargeException)
        {
            // The body turned out to be over the line while it was being read - the case a
            // declared length cannot catch.
            await RefuseAsync(context, limit);
        }
    }

    /// <summary>
    /// Answers 413 with the reason, in the same shape every other error of this API uses.
    /// </summary>
    private async Task RefuseAsync(HttpContext context, long limit)
    {
        if (context.Response.HasStarted)
        {
            // The response is already on the wire, so there is nothing left to answer with.
            return;
        }

        _logger.LogWarning(
            "Refused {Method} {Path}: the request body is over the {Limit} byte limit",
            context.Request.Method,
            context.Request.Path,
            limit);

        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "The request body is too large",
                Detail = $"The request body must not exceed {limit} bytes.",
                Extensions = { ["traceId"] = context.TraceIdentifier }
            });
    }
}
