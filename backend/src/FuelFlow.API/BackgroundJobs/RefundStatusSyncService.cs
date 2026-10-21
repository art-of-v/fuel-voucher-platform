using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

public class RefundStatusSyncService
{
private readonly ApplicationDbContext _context;
       private readonly IMonobankClientFactory _monobankClientFactory;
       private readonly ILogger<RefundStatusSyncService> _logger;
       private readonly NotificationDispatcher _notifications;

       public RefundStatusSyncService(
           ApplicationDbContext context,
           IMonobankClientFactory monobankClientFactory,
           ILogger<RefundStatusSyncService> logger)
           : this(context, monobankClientFactory, logger, NotificationDispatcher.Disabled)
       {
       }

       /// <summary>
       /// The three-argument constructor is kept and delegates here rather than the notifications
       /// being an optional fourth parameter: several tests proxy this class with
       /// <c>Mock.Of&lt;RefundStatusSyncService&gt;(null, null, null)</c>, and an optional parameter
       /// leaves Moq unable to pick a constructor for that call. An overload it can match exactly
       /// keeps them working.
       /// </summary>
       public RefundStatusSyncService(
           ApplicationDbContext context,
           IMonobankClientFactory monobankClientFactory,
           ILogger<RefundStatusSyncService> logger,
           NotificationDispatcher notifications)
       {
           _context = context;
           _monobankClientFactory = monobankClientFactory;
           _logger = logger;
           _notifications = notifications;
       }


    /// <summary>
    /// The acquirer's reference for each refund: the order's idempotency key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be <c>refunds.ext_ref</c>, a copy of the order's key taken when the refund was
    /// written. With one book instead of two there is nothing to copy it onto, and copying it onto
    /// <c>payments</c> would have carried the drift forward for the sake of a field that is one join
    /// away. So the key is read from the order - the thing that actually owns it.
    /// </para>
    /// <para>
    /// Loaded once per batch rather than once per refund, which is strictly fewer round trips than
    /// the previous code needed only because it had the copy to hand. The <c>?? Id.ToString()</c>
    /// fallback is the same one <c>RefundOrderCommandHandler</c> used when it wrote the copy, so a
    /// refund on an order with no idempotency key is matched on the same string as before - which
    /// matters, because these strings are compared against what the acquirer echoes back.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, string>> LoadExtRefsAsync(
        IReadOnlyCollection<Payment> refunds,
        CancellationToken cancellationToken)
    {
        var orderIds = refunds.Select(p => p.OrderId).Distinct().ToList();

        var byOrder = await _context.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.IdempotencyKey })
            .ToDictionaryAsync(o => o.Id, o => o.IdempotencyKey ?? o.Id.ToString(), cancellationToken);

return refunds
                // A refund always has an order, so this filter only excludes the shape the column can
                // now take for other reasons. Written here rather than as a cast because "refunds
                // without an order" would be a real finding, not a null to silence.
                .Where(p => p.OrderId != null && byOrder.ContainsKey(p.OrderId.Value))
                .GroupBy(p => p.Id)
                .ToDictionary(g => g.Key, g => byOrder[g.First().OrderId!.Value]);
    }

    public virtual async Task SyncPendingRefundsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = SystemClock.UtcNow.AddHours(-24);
        // AsTracking is redundant but harmless: the default is TrackAll (DatabaseSetup). Kept
        // mutated below and must be persisted by SaveChanges.
        var staleRefunds = await _context.Payments
            .AsTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && p.Status == PaymentStatus.Pending
                     && p.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (staleRefunds.Count > 0)
        {
            // The orders and their delivered counts, in two lookups for the whole batch rather
            // than two per stale refund.
            var staleOrderIds = staleRefunds
                .Where(r => r.OrderId.HasValue)
                .Select(r => r.OrderId!.Value)
                .Distinct()
                .ToArray();

            var staleOrders = await _context.Orders
                .Where(o => staleOrderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o, cancellationToken);

            var staleDeliveredCounts = await _context.Fulfillments
                .Where(f => staleOrderIds.Contains(f.OrderId))
                .GroupBy(f => f.OrderId)
                .Select(g => new { OrderId = g.Key, Delivered = g.Count() })
                .ToDictionaryAsync(x => x.OrderId, x => x.Delivered, cancellationToken);

            foreach (var staleRefund in staleRefunds)
            {
                PaymentFactory.ApplyStatus(
                    staleRefund, PaymentStatus.Failed, observedAtUtc: null, SystemClock.UtcNow);
                staleRefund.ErrorMessage = "Timed out waiting for Monobank confirmation (refund abandoned)";
                var staleOrder = staleRefund.OrderId.HasValue ? staleOrders.GetValueOrDefault(staleRefund.OrderId.Value) : null;
                var staleDelivered = staleRefund.OrderId.HasValue ? staleDeliveredCounts.GetValueOrDefault(staleRefund.OrderId.Value) : 0;
                RevertPrematureRefundedOrderStatus(staleRefund, staleOrder, staleDelivered);
                _logger.LogWarning(
                    "Failed refund {RefundId} (invoice {InvoiceId}) timed out after 24h; status reset to Failed",
                    staleRefund.Id, staleRefund.ProviderInvoiceId);
            }
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Marked {Count} stale refunds as Failed", staleRefunds.Count);
        }

        var pendingRefunds = await _context.Payments
            .AsTracking()
            .Where(p => p.Kind == PaymentKind.Refund && p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (pendingRefunds.Count == 0)
        {
            _logger.LogDebug("No pending refunds to sync");
            return;
        }

        // One lookup for the whole batch; see LoadExtRefsAsync for why the reference is read from
        // the order rather than carried on the refund.
        var extRefs = await LoadExtRefsAsync(pendingRefunds, cancellationToken);

        _logger.LogInformation("Syncing status for {Count} pending refunds", pendingRefunds.Count);

        // One lookup for the whole batch, and one grouped count beside it. Both used to be asked
        // per refund: the order, and how much of it had been delivered. A hundred pending refunds
        // meant two hundred round trips before the first HTTP call was even made.
        var orderIds = pendingRefunds
            .Where(r => r.OrderId.HasValue)
            .Select(r => r.OrderId!.Value)
            .Distinct()
            .ToArray();

        var orders = await _context.Orders
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o, cancellationToken);

        var deliveredCounts = await _context.Fulfillments
            .Where(f => orderIds.Contains(f.OrderId))
            .GroupBy(f => f.OrderId)
            .Select(g => new { OrderId = g.Key, Delivered = g.Count() })
            .ToDictionaryAsync(x => x.OrderId, x => x.Delivered, cancellationToken);

        // Enforce the status invariant: while a refund is still in flight the order must
        // not sit in a refunded state (legacy rows written by the old premature-transition
        // code). Reconcile such orders back to their fulfillment-derived status.
        foreach (var refund in pendingRefunds)
        {
            var order = refund.OrderId.HasValue ? orders.GetValueOrDefault(refund.OrderId.Value) : null;
            var delivered = refund.OrderId.HasValue ? deliveredCounts.GetValueOrDefault(refund.OrderId.Value) : 0;
            RevertPrematureRefundedOrderStatus(refund, order, delivered);
        }

        foreach (var refund in pendingRefunds)
        {
                try
                {
                    // The order was loaded for the whole batch above.
                    var merchant = (refund.OrderId.HasValue && orders.TryGetValue(refund.OrderId.Value, out var order)
                        ? order.MonobankMerchant
                        : null) ?? MonobankMerchant.Live;
                    var monobankClient = _monobankClientFactory.ForMerchant(merchant);

                    var status = await monobankClient.GetInvoiceStatusAsync(
                        refund.ProviderInvoiceId, cancellationToken);

                    if (extRefs.TryGetValue(refund.Id, out var extRef))
                    {
                        await ApplyCancelListStatusAsync(refund, extRef, status.CancelList, cancellationToken);
                    }
                    else
                    {
                        // The order is gone. It cannot be confirmed against anything, and matching a
                        // cancel-list entry to an order that no longer exists would be a guess.
                        _logger.LogWarning(
                            "Order {OrderId} not found for refund {RefundId}; leaving it pending",
                            refund.OrderId, refund.Id);
                    }
                }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync refund {RefundId} status", refund.Id);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task SyncRefundForInvoiceAsync(
        string invoiceId,
        List<MonobankCancelListItem>? cancelList,
        CancellationToken cancellationToken = default)
    {
        if (cancelList is not { Count: > 0 })
        {
            return;
        }

        var refunds = await _context.Payments
            .AsTracking()
            .Where(p => p.Kind == PaymentKind.Refund
                     && p.Status == PaymentStatus.Pending
                     && p.ProviderInvoiceId == invoiceId)
            .ToListAsync(cancellationToken);

        if (refunds.Count == 0)
        {
            return;
        }

        var extRefs = await LoadExtRefsAsync(refunds, cancellationToken);

        foreach (var refund in refunds)
        {
            if (extRefs.TryGetValue(refund.Id, out var extRef))
            {
                await ApplyCancelListStatusAsync(refund, extRef, cancelList, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyCancelListStatusAsync(
        Payment refund,
        string extRef,
        List<MonobankCancelListItem>? cancelList,
        CancellationToken cancellationToken)
    {
        var entries = cancelList?.ToList() ?? [];

        var matchingEntry = entries
            .OrderByDescending(e => e.ModifiedDate)
            .FirstOrDefault(e => e.ExtRef == extRef);

        if (matchingEntry == null)
        {
            // #261. There used to be an unconditional fallback here: take the newest entry in the
            // cancel list, whatever it referred to. With two partial refunds on one invoice that
            // credits the wrong refund - its entry says "success", this refund is marked Completed,
            // and ApplyConfirmedRefundToOrderAsync moves the order to PartiallyRefunded on the
            // strength of a confirmation that belonged to somebody else. Silent, and it is money.
            //
            // So a fallback now has to be *provably* unambiguous: exactly one entry in the list and
            // no other refund on this invoice waiting. One entry cannot be somebody else's if there
            // is nothing else it could have been; "any entry" could.
            var siblings = await _context.Payments
                .AsNoTracking()
                .CountAsync(
                    p => p.Kind == PaymentKind.Refund
                         && p.Status == PaymentStatus.Pending
                         && p.ProviderInvoiceId == refund.ProviderInvoiceId
                         && p.Id != refund.Id,
                    cancellationToken);

            if (entries.Count == 1 && siblings == 0)
            {
                matchingEntry = entries[0];
                _logger.LogInformation(
                    "Refund {RefundId} has no extRef match; attributing the single cancel-list entry for invoice {InvoiceId}",
                    refund.Id, refund.ProviderInvoiceId);
            }
            else
            {
                // Left pending on purpose. The retry path costs nothing and a second look may well
                // match once the bank has caught up; guessing cannot be undone after it has moved an
                // order into a refunded state.
                _logger.LogWarning(
                    "No cancel-list entry matches refund {RefundId} (order {OrderId}, invoice {InvoiceId}): {Entries} entries, {Siblings} other pending refund(s). Left pending rather than crediting one that may belong to another.",
                    refund.Id, refund.OrderId, refund.ProviderInvoiceId, entries.Count, siblings);

                await _notifications.RefundSyncUnmatchedAsync(
                    refund.Id, refund.OrderId!.Value, refund.ProviderInvoiceId ?? "(none)", entries.Count, siblings, cancellationToken);

                return;
            }
        }

        if (matchingEntry == null)
        {
            return;
        }

        if (matchingEntry.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
        {
            PaymentFactory.ApplyStatus(
                refund, PaymentStatus.Succeeded, matchingEntry.ModifiedDate, SystemClock.UtcNow);
            refund.ErrorMessage = null;

            await ApplyConfirmedRefundToOrderAsync(refund, cancellationToken);
        }
        else if (matchingEntry.Status.Equals("failure", StringComparison.OrdinalIgnoreCase))
        {
            PaymentFactory.ApplyStatus(
                refund, PaymentStatus.Failed, observedAtUtc: null, SystemClock.UtcNow);
            refund.ErrorMessage = "Monobank reported refund failure";

            // One refund, so one lookup is one lookup - the batch paths above do the same work
            // for a whole batch at a time.
            var order = refund.OrderId.HasValue
                ? await _context.Orders.FirstOrDefaultAsync(o => o.Id == refund.OrderId.Value, cancellationToken)
                : null;
            var delivered = order is null
                ? 0
                : await _context.Fulfillments.CountAsync(f => f.OrderId == order.Id, cancellationToken);
            RevertPrematureRefundedOrderStatus(refund, order, delivered);
        }
        else
        {
            _logger.LogDebug(
                "Refund {RefundId} for order {OrderId} still {Status} at Monobank",
                refund.Id, refund.OrderId, matchingEntry.Status);
        }
    }

    private async Task ApplyConfirmedRefundToOrderAsync(Payment refund, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == refund.OrderId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for refund {RefundId}", refund.OrderId, refund.Id);
            return;
        }

        // Update order status based on fulfillment state
        var deliveredCount = await _context.Fulfillments
            .CountAsync(f => f.OrderId == order.Id, cancellationToken);

        var newStatus = deliveredCount > 0
            ? OrderStatus.PartiallyRefunded
            : OrderStatus.Refunded;

        ApplyOrderStatus(order, newStatus);

        _logger.LogInformation(
            "Refund {RefundId} for order {OrderId} confirmed by Monobank ({Amount} kopecks). Order status updated to {Status}",
            refund.Id, refund.OrderId, refund.AmountKopecks, newStatus);
    }

    private void RevertPrematureRefundedOrderStatus(
        Payment refund,
        Order? order,
        int deliveredCount)
    {
        if (order is null)
        {
            return;
        }

        // An order may only show a refunded state once its refund is Completed. If a
        // failed/timed-out/in-flight refund is paired with a refunded-looking order, put
        // the order back to its fulfillment-derived status so the refund can be retried
        // and the admin never sees contradictory combinations such as
        // "PartiallyRefunded + Refund pending" or "PartiallyRefunded + Refund failed".
        if (order.Status != OrderStatus.PartiallyRefunded && order.Status != OrderStatus.Refunded)
        {
            return;
        }

        var revertStatus = deliveredCount > 0
            ? OrderStatus.PartiallyFulfilled
            : OrderStatus.PendingFulfillment;

        ApplyOrderStatus(order, revertStatus);

        _logger.LogWarning(
            "Refund {RefundId} for order {OrderId} is no longer in flight. Order status reverted to {Status}",
            refund.Id, refund.OrderId, revertStatus);
    }

    private void ApplyOrderStatus(Order order, OrderStatus status)
    {
        var trackedOrder = _context.ChangeTracker.Entries<Order>()
            .FirstOrDefault(e => e.Entity.Id == order.Id)?.Entity;

        if (trackedOrder is not null)
        {
            trackedOrder.Status = status;
            trackedOrder.UpdatedAtUtc = SystemClock.UtcNow;
            if (status != OrderStatus.PartiallyFulfilled)
            {
                trackedOrder.PartiallyFulfilledSinceUtc = null;
            }
            return;
        }

        order.Status = status;
        order.UpdatedAtUtc = SystemClock.UtcNow;
        if (status != OrderStatus.PartiallyFulfilled)
        {
            order.PartiallyFulfilledSinceUtc = null;
        }
        _context.Orders.Update(order);
    }
}
