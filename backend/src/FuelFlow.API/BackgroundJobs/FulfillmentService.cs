using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Events;
using FuelFlow.Features.Vouchers.Movements;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.API.BackgroundJobs;

public partial class FulfillmentService
{

    /// <summary>
    /// How long after fulfilment a count mismatch may still be corrected automatically (#217).
    /// <para>
    /// The reconciliation infers "this delivery never happened" from a quantity mismatch alone. That
    /// inference is safe for a young order - a double webhook or a just-created bad order - and
    /// dangerous for an old one, where a wrong count strips fuel from a customer who genuinely holds
    /// it, and deletes the only record that they did. Fifteen minutes is long enough for the webhook
    /// and outbox paths to settle and short enough that a real bad order is still cleaned up.
    /// </para>
    /// </summary>
    private const int ReconciliationGraceMinutes = 15;

    /// <summary>
    /// A staff alert about an order that could not be filled from stock, waiting for the transaction
    /// to close before it is sent.
    /// </summary>
    /// <remarks>
    /// The alert is a Telegram HTTP call. A fulfilment holds a transaction open across a long stretch
    /// of work, so sending the alert from inside it means holding the transaction - and every row
    /// lock it holds - across a network round trip. It also means the alert can describe work the
    /// transaction then rolls back. Collecting the alert and sending it after the commit avoids both.
    /// </remarks>
    private sealed record PendingStockAlert(Guid OrderId, string FuelLabel, int Assigned, int Needed);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<FulfillmentService> _logger;
    private readonly RefundOrderCommandHandler _refundHandler;
    private readonly RuntimeSettingsService _settings;
    private readonly NotificationDispatcher _notifications;
    private readonly IConfiguration _configuration;

    public FulfillmentService(
        ApplicationDbContext context,
        ILogger<FulfillmentService> logger,
        RefundOrderCommandHandler refundHandler,
        RuntimeSettingsService settings,
        NotificationDispatcher notifications,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _refundHandler = refundHandler;
        _settings = settings;
        _notifications = notifications;
        _configuration = configuration;
    }

    public async Task ProcessPendingOrdersAsync(CancellationToken cancellationToken = default)
    {
        // The safety net for the ORDER_CREATED outbox. The fast path is the job the outbox
        // interceptor queued when the row was written, and it claims the event within seconds.
        // This waits longer than that, so the two never race for the same row.
        var claimCutoff = SystemClock.UtcNow - OutboxDispatchJob.SafetyNetClaimWindow;

        var unprocessedEvents = await _context.OutboxEvents
            .Where(e => !e.Processed && e.EventType == OutboxEventType.OrderCreated && e.CreatedAtUtc < claimCutoff)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (unprocessedEvents.Any())
        {
            _logger.LogInformation("Processing {Count} pending ORDER_CREATED events", unprocessedEvents.Count);

            foreach (var outboxEvent in unprocessedEvents)
            {
                try
                {
                    await ProcessOrderCreatedEventAsync(outboxEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process outbox event {EventId}", outboxEvent.Id);
                }
            }
        }
        else
        {
            _logger.LogDebug("No pending ORDER_CREATED events to process");
        }

        await FixMismatchedFulfillmentsAsync(cancellationToken);
        await ProcessOpenOrdersBackfillAsync(cancellationToken);
        await ProcessOpenRenewalOrdersBackfillAsync(cancellationToken);
    }

    private async Task ProcessOpenOrdersBackfillAsync(CancellationToken cancellationToken)
    {
        var openOrders = await _context.Orders
            .Include(o => o.LineItems)
            // Renewal orders are backfilled by ProcessOpenRenewalOrdersBackfillAsync, not here.
            // Exclude them so the buy-fuel path never assigns stock vouchers against a renewal
            // order's line items.
            .Where(o => (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
                        && o.Kind != OrderKind.Renewal)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (!openOrders.Any())
        {
            return;
        }

        _logger.LogInformation("Backfilling {Count} open orders", openOrders.Count);

        foreach (var order in openOrders)
        {
            try
            {
                await AssignVouchersToOrderAsync(order, null, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backfill order {OrderId}", order.Id);
            }
        }
    }
}
