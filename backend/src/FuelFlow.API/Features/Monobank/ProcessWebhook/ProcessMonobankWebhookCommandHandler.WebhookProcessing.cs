using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SettlePaidOrder;
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
        var moved = PaymentFactory.ApplyStatus(
            payment, status.Value, command.ModifiedDate, SystemClock.UtcNow);

        if (!moved && PaymentFactory.WouldUndoACapture(payment, status.Value))
        {
            // A late "failure" or "expired" for an invoice the acquirer already reported as paid.
            // The order refused the same transition and recorded the brand's word
            // (RecordUnappliedPaymentTruthAsync); the payment keeps its capture so the row and the
            // journal entry still agree that the money came in. Said out loud, because a failure
            // webhook with no matching failure anywhere else is exactly what someone reads next.
            _logger.LogWarning(
                "Monobank reports invoice {InvoiceId} as {ProviderStatus} after the capture {PaymentId} "
                + "was already {PaymentStatus}; the payment row was kept and the order records the "
                + "acquirer's word",
                command.InvoiceId, command.Status, payment.Id, payment.Status);
        }

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
            _metrics.MonobankWebhookDuplicate("target_status");
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

        // Time from checkout to a real payment outcome. A rising p95 means customers
        // are waiting longer for confirmation, usually before failures become visible.
        _metrics.RecordWebhookLag((SystemClock.UtcNow - order.CreatedAtUtc).TotalSeconds);

        if (targetStatus == OrderStatus.PendingFulfillment)
        {
            // What the acquirer observed, on the acquirer's own columns, before the settle core
            // saves the entity it was handed. The core writes the order's status, the outbox event
            // and the journal entries for every authority; these three say "Monobank told us, at
            // this instant, and this is the newest instant we have accepted", which is a fact only
            // an acquirer can supply.
            order.MonobankStatus = MonobankStatus.Success;
            order.LastWebhookProcessedAtUtc = SystemClock.UtcNow;
            order.LastWebhookModifiedDateUtc = command.ModifiedDate;

            await _settle.HandleAsync(new SettlePaidOrderCommand { Order = order }, cancellationToken);
        }
        else
        {
            // "expired" and "failure" are both the brand saying it took nothing. What another channel
            // brought is not its to unsay, and cancelling anyway is what orphaned 862.00 ₴ on a
            // terminal order in production on 2026-11-04 (planning #867, trigger 4): an operator had
            // placed declared money on this order 56 minutes before its unpaid invoice aged out.
            //
            // "reversed" is deliberately not part of this question. There the acquirer DID take the
            // money and has handed it back, and the row it returned is still a succeeded capture until
            // SyncPaymentAsync runs after this method - so the money this rule would count is precisely
            // the money on its way out. A return is the refund path's business, not the settle's.
            if (command.Status.Equals("expired", StringComparison.OrdinalIgnoreCase)
                || command.Status.Equals("failure", StringComparison.OrdinalIgnoreCase))
            {
                var settledByItsOwnMoney = await _trySettle.HandleAsync(
                    new TrySettlePaidOrderCommand
                    {
                        OrderId = order.Id,
                        Because = "the brand reported the invoice dead while the money on the order covers its price",
                    },
                    cancellationToken);

                if (settledByItsOwnMoney.Settled)
                {
                    // The order moved, but on the strength of its own money and not of this delivery,
                    // so the transition the brand asked for is refused and its word is recorded. The
                    // response still claims no transition of *this* message: announcing a payment
                    // Monobank never saw, or queueing a fulfilment from it, would put another channel's
                    // money in the acquirer's mouth. The settle wrote the handover itself.
                    var priceUah = FormattableString.Invariant($"{order.Price:0.00}");
                    _logger.LogWarning(
                        "Order {OrderId} was about to be Cancelled by payment {Status}, but the money on it "
                        + "covers its {Price} ₴, so it is settled instead (planning #867)",
                        order.Id, command.Status, priceUah);

                    await RecordUnappliedPaymentTruthAsync(
                        order, command.Status, command.ModifiedDate, cancellationToken);

                    return new ProcessingOutcome(
                        new ProcessMonobankWebhookResponse
                        {
                            Success = true,
                            OrderId = order.Id.ToString(),
                            PreviousStatus = previousStatus.ToString(),
                            NewStatus = order.Status.ToString(),
                            Message = "The invoice ended without payment; the money on the order settled it"
                        },
                        order,
                        TransitionedTo: null);
                }
            }

            order.Status = targetStatus.Value;
            order.UpdatedAtUtc = SystemClock.UtcNow;
            // What the brand said, rather than the enum's nearest neighbour. An invoice that aged out is
            // not a payment that failed, and a column reading `Failure` where the word was `expired` is
            // how the next reconciliation investigation is told the wrong question. This is the same
            // mapping the refused transition already records.
            order.MonobankStatus = WhatTheBrandSaid(command.Status);
            order.LastWebhookProcessedAtUtc = SystemClock.UtcNow;
            order.LastWebhookModifiedDateUtc = command.ModifiedDate;

            _logger.LogInformation("Order {OrderId} marked as Cancelled due to payment {Status}", order.Id, command.Status);

            _context.Orders.Update(order);
            await _context.SaveChangesAsync(cancellationToken);
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
    /// Persists the brand's view of the payment for a webhook whose transition we did not apply.
    /// </summary>
    /// <remarks>
    /// Two deliveries reach it: one the state machine refused, and one this handler refused on purpose
    /// because the money already on the order pays for it (planning #867, trigger 4). Both said
    /// something about the invoice and neither got to move the order's status.
    ///
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
        var observed = WhatTheBrandSaid(providerStatus);

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

    /// <summary>
    /// The order's payment column for the brand's own word about this invoice.
    /// </summary>
    /// <remarks>
    /// One mapping for the two paths that record what the brand said - the transition this handler
    /// refused, and the cancellation it applied - because the column answers the same question in both:
    /// what did the acquirer last tell us about this invoice. A word it has never used answers null,
    /// which leaves the column saying nothing rather than guessing.
    /// </remarks>
    private static MonobankStatus? WhatTheBrandSaid(string providerStatus) => providerStatus.ToLowerInvariant() switch
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
}
