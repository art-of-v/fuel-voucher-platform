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
            command.Amount != Money.ToKopecksLong(order.Price))
        {
            _metrics.MonobankWebhookFailed("amount_mismatch");
            _logger.LogError(
                "Monobank amount mismatch for order {OrderId}: expected {ExpectedKopecks}, got {ActualKopecks}",
                order.Id, Money.ToKopecksLong(order.Price), command.Amount);

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

            // Record what the provider says even though we will not act on it.
            //
            // Returning here without persisting anything is how a customer ends up charged and
            // invisible: the invoice stays payable for its whole `validity` window, they can
            // pay it long after a `failure` cancelled the order, and the `success` that follows
            // lands on a terminal state. The state machine is right to refuse - handing over
            // goods for a cancelled order is worse - but the refusal used to also discard the
            // evidence that money was taken.
            //
            // Without this write, `monobank_status` keeps whatever the last applied transition
            // said, so the order reads as unpaid, the revenue report excludes it, reconciliation
            // never polls it (it only looks at PendingPayment), and no screen offers an operator
            // anything to act on. The one remedy that exists - a manual refund - is discoverable
            // only by knowing to look, and only by querying the provider by hand.
            await RecordUnappliedPaymentTruthAsync(
                order, command.Status, command.ModifiedDate, cancellationToken);

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

        // A renewal order (it owns voucher_renewal_items rows) fulfils on a different path than a
        // buy-fuel order: it must NOT write an ORDER_CREATED event (that event drives the buy-fuel
        // handler, which would try to mint brand-new vouchers for it), and it is handed to
        // ProcessRenewalOrderAsync instead of the generic ProcessPendingOrdersAsync sweep.
        var isRenewalOrder = targetStatus == OrderStatus.PendingFulfillment
            && await _context.VoucherRenewalItems.AnyAsync(i => i.OrderId == order.Id, cancellationToken);

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

            // Buy-fuel only: the ORDER_CREATED outbox event is consumed by ProcessOrderCreatedEventAsync,
            // which allocates fresh stock vouchers. A renewal order extends/replaces existing vouchers on
            // its own path, so writing this event would double-fulfil it — skip it for renewals.
            if (!isRenewalOrder)
            {
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
            if (isRenewalOrder)
            {
                _backgroundJobClient.Enqueue<FulfillmentService>(
                    s => s.ProcessRenewalOrderAsync(order.Id, CancellationToken.None));
            }
            else
            {
                _backgroundJobClient.Enqueue<FulfillmentService>(
                    s => s.ProcessPendingOrdersAsync(CancellationToken.None));
            }
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

    /// <summary>
    /// Persists the provider's view of the payment for a webhook we refuse to act on.
    /// </summary>
    /// <remarks>
    /// Only <c>monobank_status</c> and the two webhook timestamps move. The order status, the
    /// voucher allocation and the outbox are deliberately untouched: the transition was refused
    /// for a reason, and this records the evidence without undoing that decision.
    ///
    /// A no-op when the provider says nothing new, so a retried webhook does not rewrite history
    /// or churn <c>updated_at_utc</c> on every delivery.
    /// </remarks>
    private async Task RecordUnappliedPaymentTruthAsync(
        Order order,
        string providerStatus,
        DateTime providerModifiedDateUtc,
        CancellationToken cancellationToken)
    {
        var observed = providerStatus.ToLowerInvariant() switch
        {
            "success" => MonobankStatus.Success,
            "failure" => MonobankStatus.Failure,
            // "reversed" is money that WAS taken and has since been given back, so recording it
            // as a plain failure would understate it as never-collected. Cancelled is the closest
            // the enum has, and the refund row is the authority for the rest.
            "reversed" => MonobankStatus.Cancelled,
            "expired" => MonobankStatus.Expired,
            _ => (MonobankStatus?)null
        };

        if (observed == null || order.MonobankStatus == observed)
        {
            return;
        }

        var previous = order.MonobankStatus;

        if (observed == MonobankStatus.Success)
        {
            _logger.LogWarning(
                "Order {OrderId} is {OrderStatus} but Monobank reports it as PAID ({ProviderStatus}). " +
                    "No goods will be issued - the order is terminal - so this needs a manual refund. " +
                    "Previous recorded payment status: {Previous}",
                order.Id, order.Status, providerStatus, previous);
        }

        order.MonobankStatus = observed;
        order.UpdatedAtUtc = DateTime.UtcNow;
        order.LastWebhookProcessedAtUtc = DateTime.UtcNow;
        // The provider's own modified date, not UtcNow: the stale-replay guard compares against
        // it, and stamping "now" here would make a legitimately older-but-relevant retry look
        // already-handled.
        order.LastWebhookModifiedDateUtc = providerModifiedDateUtc;
        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
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
