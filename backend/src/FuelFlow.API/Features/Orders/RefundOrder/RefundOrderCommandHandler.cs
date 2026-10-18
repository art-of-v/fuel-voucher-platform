using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FuelFlow.API.Features.Orders.RefundOrder;

public sealed class RefundOrderCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClientFactory _monobankClientFactory;
    private readonly AuditEventService _providerEventService;

    public RefundOrderCommandHandler(
        ApplicationDbContext context,
        IMonobankClientFactory monobankClientFactory,
        AuditEventService providerEventService)
    {
        _context = context;
        _monobankClientFactory = monobankClientFactory;
        _providerEventService = providerEventService;
    }

    public async Task<RefundOrderResult> HandleAsync(
        RefundOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);

        if (order is null)
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NotFound",
                ErrorMessage = "Order not found"
            };
        }

        if (string.IsNullOrEmpty(order.MonobankInvoiceId))
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NotPayable",
                ErrorMessage = "Order has no Monobank invoice to refund"
            };
        }

        // AsTracking is redundant but harmless: the default is TrackAll (DatabaseSetup). Kept
        // amount-correction branches below mutate this entity and rely on SaveChanges.
        //
        // An existing refund IS an existing refund payment - there is no second table any more.
        var existing = await _context.Payments
            .AsTracking()
            .FirstOrDefaultAsync(
                p => p.OrderId == command.OrderId && p.Kind == PaymentKind.Refund,
                cancellationToken);

        // A refund already in flight (Pending) or confirmed (Succeeded) is reported back
        // as-is and must never be cancelled twice. Only a Failed refund is retryable.
        if (existing is not null && existing.Status != PaymentStatus.Failed)
        {
            var correctedAmount = ComputeRefundAmountKopecks(order);

            if (correctedAmount > 0 && existing.AmountKopecks != correctedAmount)
            {
                existing.AmountKopecks = correctedAmount;
                existing.AmountUah = Money.FromKopecks(correctedAmount);
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Only a refund confirmed by Monobank (Completed) may move the order into a
            // refunded state. While the refund is still Processing the order keeps its
            // fulfillment-derived status, otherwise the admin sees the contradictory
            // "PartiallyRefunded + Refund pending" combination.
            if (existing.Status == PaymentStatus.Succeeded)
            {
                // Deliberately does NOT opt out of FulfillmentConfiguration's global
                // `HasQueryFilter(e => e.ReleasedAtUtc == null)`. A delivery the reconciliation job
                // released - FulfillmentService writes ReleasedAtUtc with reason
                // "quantity_mismatch" - is not a delivery the customer received, so it must not
                // count here either: an order whose every fulfillment was released delivered
                // nothing, and Refunded is the honest status for it.
                //
                // ComputeUnfulfilledLines below reaches the same rule the other way round, by
                // filtering ReleasedAtUtc by hand, because it reads the navigation off an order
                // loaded with IgnoreQueryFilters() and that load also switches this filter off.
                // Two mechanisms for one rule is fragile, and worth collapsing into a named
                // predicate - but NOT by adding IgnoreQueryFilters here, which is what an audit
                // reading the missing opt-out suggests. That was tried: it reinstates the S9 bug
                // pinned by HandleAsync_ShouldNotCountReleasedFulfillmentsAsDelivered, where a
                // customer who paid for two units and received one was told nothing remained to be
                // returned.
                var deliveredCount = await _context.Fulfillments
                    .CountAsync(f => f.OrderId == order.Id, cancellationToken);

                var targetStatus = deliveredCount > 0
                    ? OrderStatus.PartiallyRefunded
                    : OrderStatus.Refunded;

                if (order.Status != targetStatus)
                {
                    ApplyOrderStatus(order, targetStatus);
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            return new RefundOrderResult
            {
                RefundId = existing.Id,
                // `existing` is the refund of a capture this handler already resolved through
                // `order.Id`, so its order is this one. Read from the order rather than cast off the
                // payment: the payment's own column is now nullable, and a refund without an order
                // would mean the invariant broke rather than that there is nothing to report.
                OrderId = order.Id,
                AmountKopecks = existing.AmountKopecks,
                // The refund vocabulary, not the payment's. This string is an API contract the admin
                // and the tests read, and it has always said "Processing" - a refund that is a
                // payment of kind 'Refund' being Pending is the same state, under the old word.
                Status = existing.Status == PaymentStatus.Pending ? "Processing" : "Completed",
                ErrorMessage = existing.ErrorMessage
            };
        }

        var refundableAmount = ComputeRefundAmountKopecks(order);

        // Safety cap: even when the caller (admin UI or the automatic
        // partial-fulfillment job) supplies an amount, never refund more than
        // the unfulfilled value left on the order — an over-refund would hand
        // back money for vouchers that were already delivered.
        var amount = Math.Min(command.AmountKopecks ?? refundableAmount, refundableAmount);

        if (amount <= 0)
        {
            return new RefundOrderResult
            {
                OrderId = command.OrderId,
                AmountKopecks = 0,
                Status = "NothingToRefund",
                // Name the reason. "No unfulfilled value remains" is the correct answer for a
                // fully-fulfilled order and a misleading one for an order nobody ever paid for -
                // and the admin UI surfaces this string verbatim, so a vague message here is
                // what turns a predictable refusal into a support question.
                ErrorMessage = order.MonobankStatus != MonobankStatus.Success
                    ? $"Order was not paid (brand status: {order.MonobankStatus?.ToString() ?? "unknown"}); there is nothing to return"
                    : "No unfulfilled value remains on this order"
            };
        }

        var extRef = order.IdempotencyKey ?? order.Id.ToString();

        // The capture this refund reverses. payments.refund_parent links back to exactly this row,
        // so it is what makes the refund answerable as "money back against this exact charge"
        // rather than only "money back against this order".
        //
        // Only Succeeded counts. A Pending or Expired capture has no money in it to return.
        var capture = await _context.Payments
            .Where(p => p.OrderId == order.Id
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (capture is null)
        {
            // An order that predates the payments table carries its payment in four columns of its
            // own - invoice id, brand status, price - which is precisely what that table was created
            // to replace. Those columns are real evidence, so they are promoted into a proper capture
            // row rather than the refund being refused: refusing would leave a genuinely paid
            // customer unable to be refunded, which is the worst possible answer.
            //
            // The brand status is the proof. The order passed the refundability gate above, which
            // requires MonobankStatus.Success, so a capture back-filled here is asserting something
            // already recorded, not inventing a payment.
            if (order.MonobankInvoiceId is null || order.MonobankStatus != MonobankStatus.Success)
            {
                return new RefundOrderResult
                {
                    OrderId = order.Id,
                    AmountKopecks = 0,
                    Status = "NotPayable",
                    ErrorMessage = "No succeeded capture payment to reverse"
                };
            }

            capture = PaymentFactory.CreateCapture(
                order,
                order.MonobankMerchant ?? MonobankMerchant.Live,
                order.MonobankInvoiceId,
                DateTime.UtcNow);
            PaymentFactory.ApplyStatus(
                capture,
                PaymentStatus.Succeeded,
                order.UpdatedAtUtc,
                DateTime.UtcNow);

            _context.Payments.Add(capture);
        }

        // The refund itself. There is no second row: a refund IS a payment of kind 'Refund', and this is
        // the only write. A previously Failed refund is retried in place rather than minting a
        // second payment, so one failed attempt stays one record that comes back to life.
        //
        // `existing` above is that same row, already loaded and tracked, so it is reused rather than
        // re-read - one round-trip fewer, and the instance the assignments below land on is the one
        // the change tracker knows about.
        //
        // The reload this replaces looked correct only because DatabaseSetup registers TrackAll: it
        // relied on the identity map to hand back the instance loaded at the top of the method. Under
        // any other tracking default that second load materialises a DETACHED copy instead, and
        // SaveChanges silently drops every assignment made to it - status back to Pending,
        // ErrorMessage cleared, the amount correction. The handler still calls the acquirer and still
        // answers "Processing", so a retry would report success while the row stayed Failed and the
        // attempt was never recorded. That is the read-modify-write trap this repo has been bitten by
        // five times; the guard test only scans for a marker per FILE, so the AsTracking() above
        // satisfied it while this load went unmarked.
        //
        // AsTracking on the fallback covers the same hole for a refund written by a concurrent
        // request between the two reads: that row arrives detached under the same default.
        var refundPayment = existing ?? await _context.Payments
            .AsTracking()
            .Where(p => p.OrderId == order.Id && p.Kind == PaymentKind.Refund)
            .FirstOrDefaultAsync(cancellationToken);

        if (refundPayment is null)
        {
            refundPayment = PaymentFactory.CreateRefund(
                Guid.CreateVersion7(),
                order,
                capture,
                amount,
                command.ChangedByUserId,
                command.ChangedByUserName,
                command.IsAutomatic,
                DateTime.UtcNow);

            _context.Payments.Add(refundPayment);
        }
        else
        {
            refundPayment.AmountUah = Money.FromKopecks(amount);
            refundPayment.AmountKopecks = amount;
            PaymentFactory.ApplyStatus(
                refundPayment, PaymentStatus.Pending, observedAtUtc: null, DateTime.UtcNow);
            refundPayment.ErrorMessage = null;
            refundPayment.IsAutomatic = command.IsAutomatic;
            if (command.ChangedByUserId is not null)
            {
                refundPayment.CreatedByUserId = command.ChangedByUserId;
            }
            if (!string.IsNullOrWhiteSpace(command.ChangedByUserName))
            {
                refundPayment.CreatedByUserName = command.ChangedByUserName;
            }
            refundPayment.UpdatedAtUtc = DateTime.UtcNow;
        }



        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (existing is null)
        {
            // A concurrent refund request (e.g. admin double-click) won the race past the
            // existence check; the partial unique index rejects the second insert.
            // Report the winning refund instead of surfacing a 500.
            _context.ChangeTracker.Clear();

            var winner = await _context.Payments
                .FirstOrDefaultAsync(
                    p => p.OrderId == order.Id && p.Kind == PaymentKind.Refund,
                    cancellationToken);

            if (winner is null)
            {
                throw;
            }

            return new RefundOrderResult
            {
                RefundId = winner.Id,
                // Same reasoning as the idempotent branch above: `winner` is a refund of this order's
                // capture, so the order is the one already in hand.
                OrderId = order.Id,
                AmountKopecks = winner.AmountKopecks,
                Status = winner.Status == PaymentStatus.Pending ? "Processing" : "Completed",
                ErrorMessage = winner.ErrorMessage
            };
        }

        var changedByUserId = command.ChangedByUserId ?? Guid.Empty;
        var changedByUserName = command.ChangedByUserName ?? (command.IsAutomatic ? "System" : null);

        try
        {
            var merchant = order.MonobankMerchant ?? MonobankMerchant.Live;
            var monobankClient = _monobankClientFactory.ForMerchant(merchant);

            // The RETURN receipt: the goods going back. Monobank documents `items` on invoice/cancel
            // as required when fiscalisation (ПРРО) is active, and it is sent always so that
            // activating the link is not the day refunds start failing. Built from the same
            // breakdown that produced the amount, so the receipt and the money cannot disagree.
            var unfulfilledLines = ComputeUnfulfilledLines(order);
            var fuelNames = await MonobankBasketBuilder.FuelNamesAsync(
                _context, unfulfilledLines.Select(l => l.FuelTypeId), cancellationToken);

            var returnItems = MonobankBasketBuilder.Build(unfulfilledLines.Select(l => (
                l.Brand, l.FuelTypeId,
                fuelNames.GetValueOrDefault(l.FuelTypeId),
                l.Liters, l.UnfulfilledUnits, l.UnitPrice,
                l.UnfulfilledUnits * l.UnitPrice)));

            var response = await monobankClient.CancelInvoiceAsync(
                order.MonobankInvoiceId,
                // `amount` is ALREADY kopecks - it came off a refund column, not off a UAH price.
                // It becomes a long here purely because Monobank's JSON contract is integers, and
                // going through a named conversion keeps that boundary visible instead of leaving a
                // bare cast in the call. It must NOT be routed through Money.ForMonobank, which takes
                // UAH: doing that multiplied every refund by 100, and RefundOrderCommandHandlerTests
                // caught it on the first run.
                Money.KopecksForWire(amount),
                extRef,
                returnItems,
                cancellationToken);

            // Do NOT update order status here - wait for Monobank confirmation via RefundStatusSyncService
            // Order status update is delayed to avoid confusing "PartiallyRefunded + Refund pending" state
            // Order status will be updated by RefundStatusSyncService when Monobank confirms

            refundPayment.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refundPayment.Id.ToString(),
                eventType: "RefundRequested",
                oldValue: null,
                newValue: JsonSerializer.Serialize($"{amount} kopecks (order {order.Id})"),
                changedByUserId: changedByUserId,
                changedByUserName: changedByUserName,
                summary: command.IsAutomatic
                    ? $"Automatic refund of {amount} kopecks for partially fulfilled order"
                    : $"Manual refund of {amount} kopecks requested by admin",
                providerId: order.LineItems.FirstOrDefault()?.Brand ?? "unknown",
                ct: cancellationToken);

            return new RefundOrderResult
            {
                RefundId = refundPayment.Id,
                OrderId = order.Id,
                AmountKopecks = amount,
                Status = "Processing",
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            PaymentFactory.ApplyStatus(
                refundPayment, PaymentStatus.Failed, observedAtUtc: null, DateTime.UtcNow);
            // error_message column is limited to 500 chars; a longer brand exception
            // (e.g. a full HTML error body) would make this SaveChanges throw and turn a
            // handled refund failure into an unhandled 500.
            // error_message is 500 chars; a longer brand exception (e.g. a full HTML error
            // body) would make this SaveChanges throw and turn a handled refund failure into an
            // unhandled 500.
            refundPayment.ErrorMessage = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
            refundPayment.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _providerEventService.RecordEventAsync(
                aggregateType: "Refund",
                aggregateId: refundPayment.Id.ToString(),
                eventType: "RefundFailed",
                oldValue: null,
                newValue: JsonSerializer.Serialize(ex.Message),
                changedByUserId: changedByUserId,
                changedByUserName: changedByUserName,
                summary: $"Refund of {amount} kopecks for order {order.Id} failed: {ex.Message}",
                providerId: order.LineItems.FirstOrDefault()?.Brand ?? "unknown",
                ct: cancellationToken);

            return new RefundOrderResult
            {
                RefundId = refundPayment.Id,
                OrderId = order.Id,
                AmountKopecks = amount,
                Status = "Failed",
                ErrorMessage = ex.Message
            };
        }
    }

    private void ApplyOrderStatus(Order order, OrderStatus status)
    {
        var trackedOrder = _context.ChangeTracker.Entries<Order>()
            .FirstOrDefault(e => e.Entity.Id == order.Id)?.Entity;

        if (trackedOrder is not null)
        {
            trackedOrder.Status = status;
            trackedOrder.UpdatedAtUtc = DateTime.UtcNow;
            if (status != OrderStatus.PartiallyFulfilled)
            {
                trackedOrder.PartiallyFulfilledSinceUtc = null;
            }
            return;
        }

        order.Status = status;
        order.UpdatedAtUtc = DateTime.UtcNow;
        if (status != OrderStatus.PartiallyFulfilled)
        {
            order.PartiallyFulfilledSinceUtc = null;
        }
        _context.Orders.Update(order);
    }

    /// <summary>Total ordered value in kopecks (all line items, regardless of fulfillment).</summary>
    internal static decimal ComputeTotalValueKopecks(Order order) =>
        Money.ForMonobank(order.LineItems.Sum(li => li.UnitPrice * li.Quantity));

    /// <summary>Value of delivered vouchers in kopecks (total ordered value minus unfulfilled value).</summary>
    /// <remarks>
    /// The unpaid case cannot be derived from the subtraction below. Once an unpayable order
    /// reports nothing refundable, `total - 0` would read as "delivered in full" and count an
    /// abandoned checkout as fulfilled revenue in both GetReport and GetReconciliation. Nothing
    /// was delivered, so the answer is zero.
    /// </remarks>
    internal static decimal ComputeFulfilledValueKopecks(Order order) =>
        !WasPaid(order) ? 0m : ComputeTotalValueKopecks(order) - ComputeRefundAmountKopecks(order);

    /// <summary>
    /// Whether the brand confirmed this order was paid.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed on the brand's status rather than the order status: an order can be
    /// Cancelled and still have been paid (the invoice stays payable for its whole validity
    /// window), and that is exactly the case where value must still be accounted for.
    /// </remarks>
    private static bool WasPaid(Order order) => order.MonobankStatus == MonobankStatus.Success;

    /// <summary>One fuel line of an order, and how many of its units the customer never received.</summary>
    internal readonly record struct UnfulfilledLine(
        string Brand,
        string FuelTypeId,
        decimal Liters,
        int UnfulfilledUnits,
        decimal UnitPrice);

    /// <summary>
    /// What is going back, per fuel line. The single source for both the money and the receipt
    /// lines, so the return receipt cannot describe a different refund from the one we charge for.
    /// </summary>
    internal static List<UnfulfilledLine> ComputeUnfulfilledLines(Order order)
    {
        // A company handover never captured money (Price is 0), so there is nothing to give back.
        // The write path is already blocked upstream — such an order carries no Monobank invoice —
        // so this only keeps a displayed amount from implying otherwise.
        if (order.Kind == OrderKind.ReceivedFromCompany) return [];

        // Nothing to return unless the brand says it took the money.
        //
        // Without this, every order that exists but was never paid reports its FULL price as
        // refundable: an unpaid checkout, and an order whose invoice creation failed. The admin
        // button is gated purely on this number (admin.tsx:2629), so both render as a live action
        // and both fail on the brand's terms instead of ours — an operator clicks "refund" on
        // an order nobody paid for and gets a Monobank error back.
        //
        // Keyed on the brand's status rather than the order status on purpose. An order can
        // be Cancelled and still be owed a refund: the invoice stays payable for its whole
        // validity window, so a customer can pay one long after a failure cancelled it. Gating on
        // OrderStatus would have removed the only remedy for exactly that case.
        if (!WasPaid(order)) return [];

        var grouped = order.LineItems
            .GroupBy(li => (li.Brand, li.FuelTypeId, li.Liters))
            .Select(g => new
            {
                g.Key.Brand,
                g.Key.FuelTypeId,
                g.Key.Liters,
                OrderedUnits = g.Sum(li => li.Quantity),
                UnitPrice = g.Max(li => li.UnitPrice)
            })
            .ToList();

        var unfulfilled = new List<UnfulfilledLine>();

        foreach (var group in grouped)
        {
            // ReleasedAtUtc has to be excluded here by hand, because the query above calls
            // IgnoreQueryFilters() - which is why it sees soft-deleted orders - and that also
            // switches off Fulfillment's global `ReleasedAtUtc == null` filter. That filter exists
            // for exactly this reason (see FulfillmentConfiguration): a delivery the reconciliation
            // job released is not a delivery the customer received.
            //
            // Without it, every order the reconciliation job has touched counts its released
            // fulfillments as delivered, fulfilledUnits ends up >= OrderedUnits, and the refundable
            // value collapses to zero - so a customer who paid for two units and received one is
            // told "no unfulfilled value remains" and nothing is returned. Found on the S9 refund
            // test: the admin showed "0.00 грн" against an order with half its value undelivered.
            var fulfilledUnits = order.Fulfillments
                .Count(f => f.ReleasedAtUtc is null
                    && f.Voucher is not null
                    && string.Equals(f.Voucher!.Brand, group.Brand, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == group.FuelTypeId
                    && f.Voucher.Liters == group.Liters);

            var unfulfilledUnits = group.OrderedUnits - fulfilledUnits;
            if (unfulfilledUnits > 0)
            {
                unfulfilled.Add(new UnfulfilledLine(
                    group.Brand, group.FuelTypeId, group.Liters, unfulfilledUnits, group.UnitPrice));
            }
        }

        return unfulfilled;
    }

    internal static decimal ComputeRefundAmountKopecks(Order order)
    {
        var unfulfilledValue = ComputeUnfulfilledLines(order)
            .Sum(l => l.UnfulfilledUnits * l.UnitPrice);

        return Money.ForMonobank(unfulfilledValue);
    }
}
