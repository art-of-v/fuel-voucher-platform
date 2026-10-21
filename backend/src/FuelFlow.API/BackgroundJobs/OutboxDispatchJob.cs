using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Dispatches one outbox event, by id.
/// </summary>
/// <remarks>
/// <para>
/// The three event types that anything consumes. The other members of
/// <see cref="OutboxEventType"/> are recorded and never processed - they are the audit trail, not
/// work - and a job for one of those does nothing, which is why the poller that used to run
/// every minute had three separate branches to keep in step.
/// </para>
/// <para>
/// <b>Re-reading the row, not carrying the payload.</b> The event is fetched by id and dispatched
/// only if it is still unprocessed. A duplicate enqueue - the safety net and the fast path
/// racing, or a job retried after a crash - finds the row already processed and does nothing.
/// </para>
/// <para>
/// <b>One event at a time.</b> The job handles a single event rather than a batch, because that
/// is what makes the retry honest: a job that failed half way through fifty events has to know
/// which fifty it was.
/// </para>
/// </remarks>
public sealed class OutboxDispatchJob
{
    /// <summary>
    /// How old an unprocessed event has to be before the safety net claims it.
    /// </summary>
    /// <remarks>
    /// The fast path - the job the interceptor queued - claims an event within seconds of it
    /// being written. The safety net waits longer than that, so the two never race for the same
    /// row. Without this the two would both read an unprocessed event and both act on it, which
    /// for <c>OrderFulfilled</c> means the customer gets the notification twice.
    /// </remarks>
    public static readonly TimeSpan SafetyNetClaimWindow = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _context;
    private readonly FulfillmentService _fulfillment;
    private readonly NotificationService _notifications;
    private readonly ILogger<OutboxDispatchJob> _logger;

    public OutboxDispatchJob(
        ApplicationDbContext context,
        FulfillmentService fulfillment,
        NotificationService notifications,
        ILogger<OutboxDispatchJob> logger)
    {
        _context = context;
        _fulfillment = fulfillment;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task DispatchAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var outboxEvent = await _context.OutboxEvents
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (outboxEvent is null)
        {
            // The row is not there: the transaction that wrote it rolled back, or it was deleted.
            // Either way there is nothing to dispatch.
            _logger.LogDebug("Outbox event {EventId} is not there; nothing to dispatch", eventId);
            return;
        }

        if (outboxEvent.Processed)
        {
            // The safety net, or a retried job, got here first.
            _logger.LogDebug("Outbox event {EventId} is already processed", eventId);
            return;
        }

        _logger.LogInformation("Dispatching outbox event {EventId} of type {EventType}", eventId, outboxEvent.EventType);

        switch (outboxEvent.EventType)
        {
            case OutboxEventType.OrderCreated:
                await _fulfillment.ProcessOrderCreatedEventAsync(outboxEvent, cancellationToken);
                break;

            case OutboxEventType.OrderFulfilled:
                await _notifications.ProcessEventAsync(outboxEvent, cancellationToken);
                break;

            case OutboxEventType.VoucherIssued:
                await _notifications.ProcessVoucherIssuedEventAsync(outboxEvent, cancellationToken);
                break;

            default:
                // Recorded, never consumed. Marking it processed would be a lie about work done;
                // leaving it unprocessed keeps the audit trail honest and lets a future consumer
                // pick it up.
                _logger.LogDebug(
                    "Outbox event {EventId} of type {EventType} has no consumer; leaving it unprocessed",
                    eventId,
                    outboxEvent.EventType);
                return;
        }
    }
}