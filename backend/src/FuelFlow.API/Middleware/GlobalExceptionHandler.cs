using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Middleware;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly NotificationDispatcher _notifications;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        NotificationDispatcher notifications)
    {
        _logger = logger;
        _notifications = notifications;
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

        // Only genuine server faults are worth a message. The 4xx cases above are the
        // codebase's deliberate validation and not-found channels - they are expected
        // during normal operation and would drown out real failures.
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            await _notifications.UnhandledExceptionAsync(
                exception,
                $"{context.Request.Method} {context.Request.Path}",
                cancellationToken);
        }

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
