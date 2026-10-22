using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed partial class ProcessMonobankWebhookCommandHandler
{
    /// <summary>
    /// The money step: the order's transition, the outbox event that hands it to fulfilment, the
    /// journal entries that book the money and the payment row that records it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Those four are ONE unit of work, and the transaction is what makes them one. EF opens a
    /// transaction per <c>SaveChanges</c> and nothing else did, so the order was committed
    /// <c>PendingFulfillment</c> - with its outbox event queued - <em>before</em> the receipt was
    /// posted to the journal. A journal failure (a closed accounting period, an unmapped event, the
    /// balance trigger refusing the entry) therefore left an order waiting for fuel, a queued
    /// fulfilment and no entry in the books: the fulfilment job then issued fuel for money the
    /// books had never recorded, and the hole was found at month end.
    /// </para>
    /// <para>
    /// The refund sync runs before the transaction opens, in its own commit. It asks the acquirer
    /// what happened to a refund, and holding a transaction open across an HTTP call to Monobank is
    /// how a webhook ends up holding locks on the orders table while the acquirer takes its time.
    /// </para>
    /// </remarks>
    private async Task<ProcessingOutcome> RunMoneyStepAsync(
        ProcessMonobankWebhookCommand command,
        WebhookEvent stored,
        CancellationToken cancellationToken)
    {
        await _refundStatusSyncService.SyncRefundForInvoiceAsync(
            command.InvoiceId,
            command.CancelList,
            cancellationToken);

        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // it: a transient failure retries the money step rather than failing the delivery, and the
        // retry writes the order's transition, the outbox event and the journal entries once.
        //
        // The order's own state is the answer to "did it land?": the money step exists to move the
        // order past the payment gate, so an order that is no longer waiting for its payment has
        // been through it - whether this attempt wrote that or an earlier delivery did.
        try
        {
            return await _context.ExecuteReplaySafeAsync<ProcessingOutcome>(
                async ct =>
                {
                    var processed = await ProcessAsync(command, ct);
                    await SyncPaymentAsync(command, processed.Response, stored.Id, ct);

                    return processed;
                },
                cancellationToken,
                verifySucceeded: async ct => await _context.Orders
                    .AsNoTracking()
                    .AnyAsync(
                        o => o.MonobankInvoiceId == command.InvoiceId
                            && o.Status != OrderStatus.PendingPayment,
                        ct));
        }
        catch
        {
            // The rollback undid the DATABASE; the change tracker still holds what it was about to
            // write. The next save - the one that records the delivery's failure - would re-apply
            // the order's status change and re-insert the outbox event and the journal entries,
            // which is the very thing the rollback was for. The tracker is emptied, and the delivery
            // row is attached again, so what remains to be written is exactly what still has to be
            // written: the outcome of the delivery.
            _context.ChangeTracker.Clear();
            _context.WebhookEvents.Attach(stored);

            throw;
        }
    }

    /// <summary>
    /// What happens once the money step is committed: the alert, and the handover to fulfilment.
    /// </summary>
    /// <remarks>
    /// Both are side effects that must not run before the commit. A fulfilment queued inside the
    /// transaction can start before the order is committed and find nothing to fulfil; an alert
    /// that says "paid" for a transaction that was rolled back is a lie an operator has to disprove.
    /// </remarks>
    private async Task AnnounceAsync(
        ProcessMonobankWebhookCommand command,
        ProcessingOutcome processed,
        CancellationToken cancellationToken)
    {
        if (processed.TransitionedTo is null || processed.Order is null)
        {
            return;
        }

        var order = processed.Order;

        if (processed.TransitionedTo == OrderStatus.PendingFulfillment)
        {
            await _notifications.PaymentSucceededAsync(order.Id, order.Price, cancellationToken);
        }
        else if (processed.TransitionedTo == OrderStatus.Cancelled)
        {
            await _notifications.PaymentFailedAsync(order.Id, command.Status, cancellationToken);
        }

        if (processed.TransitionedTo == OrderStatus.PendingFulfillment)
        {
            // A renewal order (its lines name the vouchers they renew) fulfils on a different path
            // than a buy-fuel order: it is handed to ProcessRenewalOrderAsync instead of the
            // generic ProcessPendingOrdersAsync sweep.
            if (order.Kind == OrderKind.Renewal)
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

        // After the commit, so a message can never describe a transition that was not persisted.
        _logger.LogInformation(
            "Order {OrderId} status updated from {PreviousStatus} to {NewStatus}",
            order.Id,
            processed.Response.PreviousStatus,
            processed.Response.NewStatus);
    }

    /// <summary>
    /// What the stored delivery should say about itself once processing has an answer.
    /// </summary>
    /// <remarks>
    /// Only a response that actually moved an order is <see cref="WebhookReceiptStatus.Processed"/>.
    /// Everything else the handler acknowledges on purpose - a replay, a status with no mapping, a
    /// transition the state machine refused, an invoice we have no order for - is recorded as
    /// <see cref="WebhookReceiptStatus.Ignored"/>, because "we saw it and decided against it" is an
    /// answer and "we never saw it" is not. An amount mismatch is the one thing that is neither:
    /// it is a real problem nobody acted on, so it is <see cref="WebhookReceiptStatus.Failed"/> and it
    /// surfaces through the outstanding-delivery index.
    /// </remarks>
    private static WebhookReceiptStatus ReceiptStatusFor(ProcessMonobankWebhookResponse response)
    {
        if (!response.Success)
        {
            return response.ErrorCode == "AMOUNT_MISMATCH"
                ? WebhookReceiptStatus.Failed
                : WebhookReceiptStatus.Ignored;
        }

        var moved = response.PreviousStatus is not null
            && response.NewStatus is not null
            && response.PreviousStatus != response.NewStatus;

        return moved ? WebhookReceiptStatus.Processed : WebhookReceiptStatus.Ignored;
    }

    private static string? ErrorFor(ProcessMonobankWebhookResponse response)
        => response.Success ? null : response.Message;
}
