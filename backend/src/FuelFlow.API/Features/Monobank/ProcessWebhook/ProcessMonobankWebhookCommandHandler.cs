using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Ledger;
using FuelFlow.Payments;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Database;
using FuelFlow.Persistence;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FuelFlow.API.Features.Monobank.ProcessWebhook;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed partial class ProcessMonobankWebhookCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessMonobankWebhookCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly RefundStatusSyncService _refundStatusSyncService;
    private readonly FuelFlowMetrics _metrics;
    private readonly NotificationDispatcher _notifications;
    private readonly JournalPostingService _journal;

    public ProcessMonobankWebhookCommandHandler(
        ApplicationDbContext context,
        ILogger<ProcessMonobankWebhookCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient,
        RefundStatusSyncService refundStatusSyncService,
        FuelFlowMetrics metrics,
        NotificationDispatcher notifications,
        JournalPostingService journal)
    {
        _context = context;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
        _refundStatusSyncService = refundStatusSyncService;
        _metrics = metrics;
        _notifications = notifications;
        _journal = journal;
    }

    /// <summary>
    /// What the processing step decided: the answer the acquirer gets, and what still has to
    /// happen once the money step is committed.
    /// </summary>
    /// <param name="Response">What the webhook answers the acquirer.</param>
    /// <param name="Order">The order the delivery concerned, when there was one.</param>
    /// <param name="TransitionedTo">
    /// The status the order was moved to, or null when no transition happened. Null is what keeps a
    /// replayed or a refused delivery from announcing a payment and from queueing a fulfilment.
    /// </param>
    private sealed record ProcessingOutcome(
        ProcessMonobankWebhookResponse Response,
        Order? Order,
        OrderStatus? TransitionedTo);

    public async Task<ProcessMonobankWebhookResponse> HandleAsync(
        ProcessMonobankWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        _metrics.MonobankWebhookReceived(command.Status.ToLowerInvariant());

        _logger.LogInformation(
            "Processing Monobank webhook for invoice {InvoiceId}, status: {Status}",
            command.InvoiceId,
            command.Status);

        // Stored before anything acts on it. Until this existed the delivery existed only for the
        // duration of the request: the body was read in the controller, handed over, and never
        // written anywhere. A customer disputing a charge had nothing to show.
        //
        // In its own commit, deliberately: the delivery has to survive a business failure, because
        // "we received it and could not act on it" is a state an operator can find.
        var outcome = await WebhookEventRecorder.RecordAsync(_context, command, _logger, cancellationToken);

        if (outcome.IsRedelivery)
        {
            // Acknowledged, not processed. Monobank retries on a non-2xx, so anything else here turns
            // one redelivery into a loop against an event we already acted on.
            return new ProcessMonobankWebhookResponse
            {
                Success = true,
                Message = "Duplicate delivery already recorded"
            };
        }

        var stored = outcome.Stored!;

        ProcessingOutcome processed;
        try
        {
            processed = await RunMoneyStepAsync(command, stored, cancellationToken);
        }
        catch (Exception ex)
        {
            // The delivery is on file and still marked outstanding, which is the point: "we received
            // it and could not act on it" is a state an operator can find, and it is the opposite of
            // the delivery silently disappearing.
            await WebhookEventRecorder.CompleteAsync(
                _context, stored, WebhookReceiptStatus.Failed, ex.Message, _logger, cancellationToken);
            throw;
        }

        // Only after the money step is committed. A notification that announces a payment the
        // transaction rolled back is a lie an operator has to disprove, and a fulfilment queued
        // before the commit can start before there is anything to fulfil.
        await AnnounceAsync(command, processed, cancellationToken);

        await WebhookEventRecorder.CompleteAsync(
            _context, stored, ReceiptStatusFor(processed.Response), ErrorFor(processed.Response), _logger, cancellationToken);

        return processed.Response;
    }

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

    /// <summary>
    /// Acknowledges a delivery that moved nothing: the order is where it was, and no payment is
    /// announced and no fulfilment is queued for it.
    /// </summary>
    private static ProcessingOutcome Ack(Order order, string message) => new(
        new ProcessMonobankWebhookResponse
        {
            Success = true,
            OrderId = order.Id.ToString(),
            PreviousStatus = order.Status.ToString(),
            NewStatus = order.Status.ToString(),
            Message = message
        },
        order,
        TransitionedTo: null);
}
