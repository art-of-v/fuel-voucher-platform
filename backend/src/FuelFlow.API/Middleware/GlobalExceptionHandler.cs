using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Middleware;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

        var (statusCode, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),

            // ArgumentException is this codebase's deliberate validation channel - handlers throw
            // it with messages written for the caller ("Quantity must be greater than 0 for every
            // item"), so the message is passed through on purpose.
            ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),

            // InvalidOperationException is almost never ours: EF Core, Npgsql and LINQ throw it
            // with text that names entity types, describes tracking state, quotes SQL and reports
            // connection problems. Echoing that to an anonymous caller turns any endpoint that can
            // be made to fault into a schema- and infrastructure-disclosure oracle. Status is kept
            // at 400 so client behaviour does not change; only the text is withheld.
            InvalidOperationException => (StatusCodes.Status400BadRequest, "The request could not be processed"),

            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                // Correlates the withheld detail with the logged exception without disclosing it.
                Extensions = { ["traceId"] = context.TraceIdentifier }
            },
            cancellationToken);

        return true;
    }
}
