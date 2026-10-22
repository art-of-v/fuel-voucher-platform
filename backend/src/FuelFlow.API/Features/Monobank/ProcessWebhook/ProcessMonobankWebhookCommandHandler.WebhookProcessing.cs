using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed partial class ProcessMonobankWebhookCommandHandler
{
    /// <summary>
    /// Moves the payment row this delivery concerns.
    /// </summary>
    /// <remarks>
    /// The order's <c>monobank_status</c> and the payment's <c>status</c> are two records of the
    /// same event and are allowed to disagree: the order says what we did about the money (a terminal
    /// order still reads paid, via <c>RecordUnappliedPaymentTruthAsync</c>), while the payment says
    /// what the acquirer did. Keeping the payment's own status is what will let the double-entry
    /// journal post revenue against money that arrived, independently of whether an order was allowed
    /// to proceed - which is precisely the case the current schema cannot represent at all.
    /// </remarks>
    private async Task SyncPaymentAsync(
        ProcessMonobankWebhookCommand command,
        ProcessMonobankWebhookResponse response,
        Guid webhookEventId,
        CancellationToken cancellationToken)
    {
        var status = PaymentFactory.FromProviderStatus(command.Status);
        if (status is null)
        {
            return;
        }

        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Provider == WebhookEventIdentity.Monobank
                                   && p.ProviderInvoiceId == command.InvoiceId
                                   && p.Kind == PaymentKind.Capture, cancellationToken);

        if (payment is null)
        {
            // Not a failure. The payment row is written when the invoice is created, so a delivery
            // with no row means the invoice predates this table or checkout failed before its save.
            // Both are legitimate, and neither belongs on a money path as an error.
            _logger.LogInformation(
                "No capture payment row for Monobank invoice {InvoiceId}; nothing to update",
                command.InvoiceId);
            return;
        }

        // The provider's modified date is the best available approximation of when the money moved
        // and is what the order already trusts for replay ordering. The statement import replaces it
        // with the acquirer's financial operation time when one arrives, which is the authority.
        PaymentFactory.ApplyStatus(payment, status.Value, command.ModifiedDate, SystemClock.UtcNow);

        // The acquirer's own figure, kept beside ours rather than only in the delivery. This is what
        // makes an amount mismatch answerable from the money row: two numbers on one row, and a
        // query that finds them differing. The database deliberately does not forbid the difference.
        payment.ProviderAmountKopecks = command.Amount;

        if (payment.WebhookEventId is null)
        {
            payment.WebhookEventId = webhookEventId;
        }

        if (payment.ProviderPayload is null && command.RawBody is not null)
        {
            payment.ProviderPayload = command.RawBody;
        }

        var advanced = response.NewStatus is not null
            && response.PreviousStatus is not null
            && response.PreviousStatus != response.NewStatus;

        if (!response.Success || (status == PaymentStatus.Succeeded && !advanced))
        {
            // Money taken, order not advanced. The common cause is a terminal order whose invoice was
            // paid late, which RecordUnappliedPaymentTruthAsync already handles by refusing the goods;
            // this says so at the payment, where an operator looking at the money will look.
            _logger.LogWarning(
                "Payment for invoice {InvoiceId} is {PaymentStatus} but the order did not advance ({Outcome})",
                command.InvoiceId, status.Value, response.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProcessingOutcome> ProcessAsync(
        ProcessMonobankWebhookCommand command,
        CancellationToken cancellationToken)
    {
        _metrics.MonobankWebhookReceived(command.Status.ToLowerInvariant());

        _logger.LogInformation(
            "Processing Monobank webhook for invoice {InvoiceId}, status: {Status}",
            command.InvoiceId,
            command.Status);

        // IncludingSoftDeleted: an invoice can outlive its order becoming hidden. A customer can
        // swipe-delete an unpaid checkout (soft delete, IsDeleted=true) while its Monobank invoice
        // stays live and payable. Without this, a later "success" callback would miss the
        // soft-deleted order (global !IsDeleted filter), log order_not_found, and leave the customer
        // charged with no voucher and no fulfillment. Mirrors RefundOrderCommandHandler's lookup.
        var order = await _context.Orders
            .IncludingSoftDeleted()
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.MonobankInvoiceId == command.InvoiceId, cancellationToken);

        if (order == null)
        {
            _metrics.MonobankWebhookFailed("order_not_found");
            _logger.LogWarning("Order not found for Monobank invoice {InvoiceId}", command.InvoiceId);
            return new ProcessingOutcome(
                new ProcessMonobankWebhookResponse
                {
                    Success = false,
                    ErrorCode = "NOT_FOUND",
                    Message = $"Order not found for invoice {command.InvoiceId}"
                },
                Order: null,
                TransitionedTo: null);
        }

        // Reject stale callbacks: a webhook whose ModifiedDate is not newer than the
        // last accepted one is a duplicate or replay and must never re-transition the order.
        if (order.LastWebhookModifiedDateUtc.HasValue &&
            command.ModifiedDate <= order.LastWebhookModifiedDateUtc.Value)
        {
            _logger.LogWarning(
                "Stale Monobank webhook for order {OrderId}: modified {ModifiedDate} not newer than last accepted {LastAccepted}",
                order.Id, command.ModifiedDate, order.LastWebhookModifiedDateUtc.Value);

            return new ProcessingOutcome(
                new ProcessMonobankWebhookResponse
                {
                    Success = true,
                    OrderId = order.Id.ToString(),
                    PreviousStatus = order.Status.ToString(),
                    NewStatus = order.Status.ToString(),
                    Message = "Stale webhook acknowledged, no transition"
                },
                order,
                TransitionedTo: null);
        }

        // For "success" callbacks the charged amount must match our server-side order price.
        if (command.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            command.Amount != Money.ForMonobank(order.Price))
        {
            _metrics.MonobankWebhookFailed("amount_mismatch");
            _logger.LogError(
                "Monobank amount mismatch for order {OrderId}: expected {ExpectedKopecks}, got {ActualKopecks}",
                order.Id, Money.ForMonobank(order.Price), command.Amount);

            return new ProcessingOutcome(
                new ProcessMonobankWebhookResponse
                {
                    Success = false,
                    ErrorCode = "AMOUNT_MISMATCH",
                    OrderId = order.Id.ToString(),
                    Message = $"Amount {command.Amount} does not match order price {order.Price}"
                },
                order,
                TransitionedTo: null);
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

            // Record what the brand says even though we will not act on it.
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
            // only by knowing to look, and only by querying the brand by hand.
            await RecordUnappliedPaymentTruthAsync(
                order, command.Status, command.ModifiedDate, cancellationToken);

            return Ack(order, "Illegal transition rejected");
        }

        var previousStatus = order.Status;
        order.Status = targetStatus.Value;
        order.UpdatedAtUtc = SystemClock.UtcNow;
        order.LastWebhookProcessedAtUtc = SystemClock.UtcNow;
        order.LastWebhookModifiedDateUtc = command.ModifiedDate;

        // Time from checkout to a real payment outcome. A rising p95 means customers
        // are waiting longer for confirmation, usually before failures become visible.
        _metrics.RecordWebhookLag((SystemClock.UtcNow - order.CreatedAtUtc).TotalSeconds);

        // A renewal order (its lines name the vouchers they renew) fulfils on a different path than a
        // buy-fuel order: it must NOT write an ORDER_CREATED event (that event drives the buy-fuel
        // handler, which would try to mint brand-new vouchers for it), and it is handed to
        // ProcessRenewalOrderAsync instead of the generic ProcessPendingOrdersAsync sweep.
var isRenewalOrder = targetStatus == OrderStatus.PendingFulfillment
                             && order.Kind == OrderKind.Renewal;

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
        }
        else if (targetStatus == OrderStatus.Cancelled)
        {
            order.MonobankStatus = MonobankStatus.Failure;
            _logger.LogInformation("Order {OrderId} marked as Cancelled due to payment {Status}", order.Id, command.Status);
        }

        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);

        // The receipt is posted INSIDE the transaction, before the commit. That is the whole point:
        // a journal failure has to leave the order where it was, not half-way to being fulfilled.
        // Only real transitions reach here - duplicates, stale replays and illegal transitions all
        // returned earlier, so each payment is reported once.
        if (targetStatus == OrderStatus.PendingFulfillment)
        {
            await PostReceiptToJournalAsync(order, cancellationToken);
        }

        return new ProcessingOutcome(
            new ProcessMonobankWebhookResponse
            {
                Success = true,
                OrderId = order.Id.ToString(),
                PreviousStatus = previousStatus.ToString(),
                NewStatus = order.Status.ToString(),
                Message = $"Order {order.Id} updated to {order.Status}"
            },
            order,
            targetStatus);
    }

    /// <summary>
    /// Persists the brand's view of the payment for a webhook we refuse to act on.
    /// </summary>
    /// <remarks>
    /// Only <c>monobank_status</c> and the two webhook timestamps move. The order status, the
    /// voucher allocation and the outbox are deliberately untouched: the transition was refused
    /// for a reason, and this records the evidence without undoing that decision.
    ///
    /// A no-op when the brand says nothing new, so a retried webhook does not rewrite history
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
        order.UpdatedAtUtc = SystemClock.UtcNow;
        order.LastWebhookProcessedAtUtc = SystemClock.UtcNow;
        // The brand's own modified date, not UtcNow: the stale-replay guard compares against
        // it, and stamping "now" here would make a legitimately older-but-relevant retry look
        // already-handled.
        order.LastWebhookModifiedDateUtc = providerModifiedDateUtc;
        _context.Orders.Update(order);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
