using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

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

        var (statusCode, detail) = exception switch
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

            // #246 detector. A concurrency token means this write did not land because the row
            // changed underneath it. 409 is the honest answer and the caller can legitimately retry.
            // It is deliberately NOT a 500: that would fire the operator alert below on every
            // collision, and the token exists to be hit - a collision is the token working, not a
            // fault. The specific entity was already logged with its key in ApplicationDbContext.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record changed while you were editing it - please try again"),

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

        // The same envelope every other failure uses: the status in `title`, the specific
        // sentence in `detail`, and the trace id that ties the withheld detail to the logged
        // exception without disclosing it.
        await ApiErrorResponse.WriteAsync(
            context,
            statusCode,
            detail,
            cancellationToken: cancellationToken);

        return true;
    }
}
