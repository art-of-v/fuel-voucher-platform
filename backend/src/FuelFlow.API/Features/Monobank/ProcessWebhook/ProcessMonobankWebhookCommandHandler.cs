using FuelFlow.API.BackgroundJobs;
using FuelFlow.Persistence;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using FuelFlow.API.Features.Monobank.ProcessWebhook;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed class ProcessMonobankWebhookCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessMonobankWebhookCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly RefundStatusSyncService _refundStatusSyncService;
    private readonly FuelFlowMetrics _metrics;
    private readonly NotificationDispatcher _notifications;

    public ProcessMonobankWebhookCommandHandler(
        ApplicationDbContext context,
        ILogger<ProcessMonobankWebhookCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient,
        RefundStatusSyncService refundStatusSyncService,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications)
    {
        _context = context;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
        _refundStatusSyncService = refundStatusSyncService;
        _metrics = metrics;
        _notifications = notifications;
    }

    public async Task<ProcessMonobankWebhookResponse> HandleAsync(
        ProcessMonobankWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        _metrics.MonobankWebhookReceived(command.Status.ToLowerInvariant());

        _logger.LogInformation(
            "Processing Monobank webhook for invoice {InvoiceId}, status: {Status}",
            command.InvoiceId,
            command.Status);

        await _refundStatusSyncService.SyncRefundForInvoiceAsync(
            command.InvoiceId,
            command.CancelList,
            cancellationToken);

        // IgnoreQueryFilters: an invoice can outlive its order becoming hidden. A customer can
        // swipe-delete an unpaid checkout (soft delete, IsDeleted=true) while its Monobank invoice
        // stays live and payable. Without this, a later "success" callback would miss the
        // soft-deleted order (global !IsDeleted filter), log order_not_found, and leave the customer
        // charged with no voucher and no fulfillment. Mirrors RefundOrderCommandHandler's lookup.
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.MonobankInvoiceId == command.InvoiceId, cancellationToken);

        if (order == null)
        {
            _metrics.MonobankWebhookFailed("order_not_found");
            _logger.LogWarning("Order not found for Monobank invoice {InvoiceId}", command.InvoiceId);
            return new ProcessMonobankWebhookResponse
            {
                Success = false,
                ErrorCode = "NOT_FOUND",
                Message = $"Order not found for invoice {command.InvoiceId}"
            };
        }

        // Reject stale callbacks: a webhook whose ModifiedDate is not newer than the
        // last accepted one is a duplicate or replay and must never re-transition the order.
        if (order.LastWebhookModifiedDateUtc.HasValue &&
            command.ModifiedDate <= order.LastWebhookModifiedDateUtc.Value)
        {
            _logger.LogWarning(
                "Stale Monobank webhook for order {OrderId}: modified {ModifiedDate} not newer than last accepted {LastAccepted}",
                order.Id, command.ModifiedDate, order.LastWebhookModifiedDateUtc.Value);

            return new ProcessMonobankWebhookResponse
            {
                Success = true,
                OrderId = order.Id.ToString(),
                PreviousStatus = order.Status.ToString(),
                NewStatus = order.Status.ToString(),
                Message = "Stale webhook acknowledged, no transition"
            };
        }

        // For "success" callbacks the charged amount must match our server-side order price.
        if (command.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            command.Amount != Money.ToKopecks(order.Price))
        {
            _metrics.MonobankWebhookFailed("amount_mismatch");
            _logger.LogError(
                "Monobank amount mismatch for order {OrderId}: expected {ExpectedKopecks}, got {ActualKopecks}",
                order.Id, Money.ToKopecks(order.Price), command.Amount);

            return new ProcessMonobankWebhookResponse
            {
                Success = false,
                ErrorCode = "AMOUNT_MISMATCH",
                OrderId = order.Id.ToString(),
                Message = $"Amount {command.Amount} does not match order price {order.Price}"
            };
        }

        var targetStatus = command.Status.ToLowerInvariant() switch
        {
            "success" => OrderStatus.PendingFulfillment,
            // "expired": the invoice's validity window elapsed with no payment. It is terminal,
            // so cancel the order - otherwise the reconciliation poll would chase it every cycle
            // until it ages out of the window, and admins would see it stuck in PendingPayment.
            "failure" or "reversed" or "expired" => OrderStatus.Cancelled,
            _ => (OrderStatus?)null
        };

        if (targetStatus == null)
        {
            _logger.LogInformation("Order {OrderId} no transition for Monobank status {Status}", order.Id, command.Status);
            return Ack(order, "No transition for non-terminal status");
        }

        if (order.Status == targetStatus)
        {
            _logger.LogInformation(
                "Order {OrderId} already in target status {Status}; duplicate webhook acknowledged",
                order.Id, targetStatus);

            return Ack(order, "Duplicate webhook acknowledged");
        }

        if (!OrderStateMachine.CanTransition(order.Status, targetStatus.Value))
        {
            _logger.LogWarning(
                "Illegal Monobank transition {From} -> {To} for order {OrderId} rejected",
                order.Status, targetStatus, order.Id);

            return Ack(order, "Illegal transition rejected");
        }

        var previousStatus = order.Status;
        order.Status = targetStatus.Value;
        order.UpdatedAtUtc = DateTime.UtcNow;
        order.LastWebhookProcessedAtUtc = DateTime.UtcNow;
        order.LastWebhookModifiedDateUtc = command.ModifiedDate;

        // Time from checkout to a real payment outcome. A rising p95 means customers
        // are waiting longer for confirmation, usually before failures become visible.
        _metrics.RecordWebhookLag((DateTime.UtcNow - order.CreatedAtUtc).TotalSeconds);

        if (targetStatus == OrderStatus.PendingFulfillment)
        {
            order.MonobankStatus = MonobankStatus.Success;

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

            // Match the orderId field with jsonb containment, NOT a substring LIKE. payload is
            // a jsonb column and Postgres has no `jsonb ~~ jsonb` (LIKE) operator, so
            // String.Contains here threw 42883 on the paid-webhook path: the ORDER_CREATED
            // event was never written, so a paid order was never handed to fulfillment.
            var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });
            var existingEvent = await _context.OutboxEvents
                .Where(e => e.EventType == OutboxEventType.OrderCreated
                         && EF.Functions.JsonContains(e.Payload, orderProbe))
                .FirstOrDefaultAsync(cancellationToken);

            if (existingEvent == null)
            {
                var firstLi = order.LineItems.FirstOrDefault();
                var outboxEvent = new OutboxEvent
                {
                    EventType = OutboxEventType.OrderCreated,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        orderId = order.Id,
                        userId = order.UserId,
                        provider = firstLi?.Provider ?? "",
                        fuelType = firstLi?.FuelTypeId ?? "",
                        liters = firstLi?.Liters ?? 0m,
                        quantity = firstLi?.Quantity ?? 0
                    }),
                    Processed = false,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _context.OutboxEvents.Add(outboxEvent);
                _logger.LogInformation("Created ORDER_CREATED outbox event for order {OrderId}", order.Id);
            }
        }
        else if (targetStatus == OrderStatus.Cancelled)
        {
            order.MonobankStatus = MonobankStatus.Failure;
            _logger.LogInformation("Order {OrderId} marked as Cancelled due to payment {Status}", order.Id, command.Status);
        }

        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);

        // After SaveChanges, so a message can never describe a transition that was not
        // persisted. Only real transitions reach here - duplicates, stale replays and
        // illegal transitions all returned earlier, so each payment is reported once.
        if (targetStatus == OrderStatus.PendingFulfillment)
        {
            await _notifications.PaymentSucceededAsync(order.Id, order.Price, cancellationToken);
        }
        else if (targetStatus == OrderStatus.Cancelled)
        {
            await _notifications.PaymentFailedAsync(order.Id, command.Status, cancellationToken);
        }

        if (order.Status == OrderStatus.PendingFulfillment)
        {
            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        _logger.LogInformation(
            "Order {OrderId} status updated from {PreviousStatus} to {NewStatus}",
            order.Id,
            previousStatus,
            order.Status);

        return new ProcessMonobankWebhookResponse
        {
            Success = true,
            OrderId = order.Id.ToString(),
            PreviousStatus = previousStatus.ToString(),
            NewStatus = order.Status.ToString(),
            Message = $"Order {order.Id} updated to {order.Status}"
        };
    }

    private static ProcessMonobankWebhookResponse Ack(Order order, string message) => new()
    {
        Success = true,
        OrderId = order.Id.ToString(),
        PreviousStatus = order.Status.ToString(),
        NewStatus = order.Status.ToString(),
        Message = message
    };
}
