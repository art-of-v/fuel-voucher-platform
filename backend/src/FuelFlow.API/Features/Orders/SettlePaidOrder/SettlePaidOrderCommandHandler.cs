using FuelFlow.Features.Ledger;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Turns a paid order into an order waiting for fuel, with the money in the books.
/// </summary>
/// <remarks>
/// <para>
/// Four writes, and they are one unit because each is meaningless without the others: the order
/// moves to <see cref="OrderStatus.PendingFulfillment"/>, a soft-deleted order comes back into view,
/// an <c>ORDER_CREATED</c> outbox event hands it to fulfilment, and the receipt is posted. The
/// caller's transaction is what makes them one - this handler opens none of its own, so a caller
/// that wraps it in <c>ExecuteReplaySafeAsync</c> gets the rollback that a paid order needs and a
/// caller that does not is committing the transition and the entry in the same unit anyway.
/// </para>
/// <para>
/// <b>Money that is taken and not in the books is worse than a payment that is retried.</b> That is
/// why the journal post is here and not in the caller's next step: the customer can pay twice, and
/// a double payment is visible on the money row, but a book that is silently short is discovered at
/// month end by somebody who cannot reconstruct which orders were paid.
/// </para>
/// <para>
/// <b>Repeating a settle is safe, and that is a property of the keys, not of this code.</b> The
/// outbox write is guarded by a containment probe on <c>payload-&gt;orderId</c>, and the journal by
/// the unique <c>(source_event, source_type, source_id)</c> index - <c>source_id</c> is the order,
/// so one order is one receipt however many deliveries, polls or operator presses name it.
/// </para>
/// </remarks>
public sealed partial class SettlePaidOrderCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SettlePaidOrderCommandHandler> _logger;
    private readonly JournalPostingService _journal;

    public SettlePaidOrderCommandHandler(
        ApplicationDbContext context,
        ILogger<SettlePaidOrderCommandHandler> logger,
        JournalPostingService journal)
    {
        _context = context;
        _logger = logger;
        _journal = journal;
    }

    public async Task HandleAsync(SettlePaidOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = command.Order;

        order.Status = OrderStatus.PendingFulfillment;
        order.UpdatedAtUtc = SystemClock.UtcNow;

        // A soft-deleted order that just got paid must come back into view: the customer was
        // charged, so the order has to be visible and fulfillable again, not stay hidden.
        if (order.IsDeleted)
        {
            order.IsDeleted = false;
            _logger.LogWarning(
                "Order {OrderId} was soft-deleted but its invoice was paid; restoring it for fulfillment",
                order.Id);
        }

        _logger.LogInformation("Order {OrderId} marked as PendingFulfillment", order.Id);

        await HandToFulfillmentAsync(order, cancellationToken);

        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);

        // Only real transitions reach here - duplicates, stale replays and refused transitions
        // return earlier in the caller - so each payment is reported once.
        await PostReceiptToJournalAsync(order, cancellationToken);
    }

    /// <summary>
    /// Queues the event that hands the order to the code which claims stock for it.
    /// </summary>
    /// <remarks>
    /// Buy-fuel orders only. A renewal order's lines name the vouchers they extend or replace and
    /// are fulfilled on their own path, so an <c>ORDER_CREATED</c> event for one would drive the
    /// buy-fuel handler into minting fresh stock vouchers on top of the renewed ones.
    /// </remarks>
    private async Task HandToFulfillmentAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.Kind == OrderKind.Renewal)
        {
            return;
        }

        // Match the orderId field with jsonb containment, NOT a substring LIKE. payload is a jsonb
        // column and Postgres has no `jsonb ~~ jsonb` operator, so String.Contains threw 42883 here:
        // the ORDER_CREATED event was never written, so a paid order was never handed to fulfilment.
        var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });
        var existingEvent = await _context.OutboxEvents
            .Where(e => e.EventType == OutboxEventType.OrderCreated
                     && EF.Functions.JsonContains(e.Payload, orderProbe))
            .FirstOrDefaultAsync(cancellationToken);

        if (existingEvent is not null)
        {
            return;
        }

        var firstLi = order.LineItems.FirstOrDefault();
        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId,
                brand = firstLi?.Brand ?? "",
                fuelType = firstLi?.FuelTypeId ?? Guid.Empty,
                liters = firstLi?.Liters ?? 0m,
                quantity = firstLi?.Quantity ?? 0
            }),
            Processed = false,
            CreatedAtUtc = SystemClock.UtcNow
        };

        _context.OutboxEvents.Add(outboxEvent);
        _logger.LogInformation("Created ORDER_CREATED outbox event for order {OrderId}", order.Id);
    }
}
