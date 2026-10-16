using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
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

    public virtual async Task SyncPendingRefundsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        // AsTracking is redundant but harmless: the default is TrackAll (DatabaseSetup). Kept
        // mutated below and must be persisted by SaveChanges.
        var staleRefunds = await _context.Refunds
            .AsTracking()
            .Where(r => r.Status == RefundStatus.Processing && r.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (staleRefunds.Count > 0)
        {
            foreach (var staleRefund in staleRefunds)
            {
                staleRefund.Status = RefundStatus.Failed;
                staleRefund.ErrorMessage = "Timed out waiting for Monobank confirmation (refund abandoned)";
                staleRefund.UpdatedAtUtc = DateTime.UtcNow;
                await RevertPrematureRefundedOrderStatusAsync(staleRefund, cancellationToken);
                _logger.LogWarning(
                    "Failed refund {RefundId} (invoice {InvoiceId}) timed out after 24h; status reset to Failed",
                    staleRefund.Id, staleRefund.InvoiceId);
            }
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Marked {Count} stale refunds as Failed", staleRefunds.Count);
        }

        var pendingRefunds = await _context.Refunds
            .AsTracking()
            .Where(r => r.Status == RefundStatus.Processing)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (pendingRefunds.Count == 0)
        {
            _logger.LogDebug("No pending refunds to sync");
            return;
        }

        _logger.LogInformation("Syncing status for {Count} pending refunds", pendingRefunds.Count);

        // Enforce the status invariant: while a refund is still in flight the order must
        // not sit in a refunded state (legacy rows written by the old premature-transition
        // code). Reconcile such orders back to their fulfillment-derived status.
        foreach (var refund in pendingRefunds)
        {
            await RevertPrematureRefundedOrderStatusAsync(refund, cancellationToken);
        }

        foreach (var refund in pendingRefunds)
        {
                try
                {
                    var order = await _context.Orders
                        .AsNoTracking()
                        .FirstOrDefaultAsync(o => o.Id == refund.OrderId, cancellationToken);

                    // Poll the merchant that issued this invoice, recorded at checkout. An order
                    // without one predates the two-merchant change, so it is a live invoice.
                    var merchant = order?.MonobankMerchant ?? MonobankMerchant.Live;
                    var monobankClient = _monobankClientFactory.ForMerchant(merchant);

                    var status = await monobankClient.GetInvoiceStatusAsync(refund.InvoiceId, cancellationToken);
                    await ApplyCancelListStatusAsync(refund, status.CancelList, cancellationToken);
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

        var refunds = await _context.Refunds
            .AsTracking()
            .Where(r => r.InvoiceId == invoiceId && r.Status == RefundStatus.Processing)
            .ToListAsync(cancellationToken);

        if (refunds.Count == 0)
        {
            return;
        }

        foreach (var refund in refunds)
        {
            await ApplyCancelListStatusAsync(refund, cancelList, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyCancelListStatusAsync(
        Refund refund,
        List<MonobankCancelListItem>? cancelList,
        CancellationToken cancellationToken)
    {
        var entries = cancelList?.ToList() ?? [];

        var matchingEntry = entries
            .OrderByDescending(e => e.ModifiedDate)
            .FirstOrDefault(e => e.ExtRef == refund.ExtRef);

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
            var siblings = await _context.Refunds
                .AsNoTracking()
                .CountAsync(
                    r => r.InvoiceId == refund.InvoiceId
                         && r.Status == RefundStatus.Processing
                         && r.Id != refund.Id,
                    cancellationToken);

            if (entries.Count == 1 && siblings == 0)
            {
                matchingEntry = entries[0];
                _logger.LogInformation(
                    "Refund {RefundId} has no extRef match; attributing the single cancel-list entry for invoice {InvoiceId}",
                    refund.Id, refund.InvoiceId);
            }
            else
            {
                // Left Processing on purpose. The retry path costs nothing and a second look may well
                // match once the bank has caught up; guessing cannot be undone after it has moved an
                // order into a refunded state.
                _logger.LogWarning(
                    "No cancel-list entry matches refund {RefundId} (order {OrderId}, invoice {InvoiceId}): {Entries} entries, {Siblings} other pending refund(s). Left Processing rather than crediting one that may belong to another.",
                    refund.Id, refund.OrderId, refund.InvoiceId, entries.Count, siblings);

                await _notifications.RefundSyncUnmatchedAsync(
                    refund.Id, refund.OrderId, refund.InvoiceId, entries.Count, siblings, cancellationToken);

                return;
            }
        }

        if (matchingEntry == null)
        {
            return;
        }

        if (matchingEntry.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Completed;
            refund.MonobankStatus = "success";
            refund.ErrorMessage = null;
            refund.UpdatedAtUtc = DateTime.UtcNow;

            await ApplyConfirmedRefundToOrderAsync(refund, cancellationToken);
        }
        else if (matchingEntry.Status.Equals("failure", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Failed;
            refund.MonobankStatus = "failure";
            refund.ErrorMessage = "Monobank reported refund failure";
            refund.UpdatedAtUtc = DateTime.UtcNow;

            await RevertPrematureRefundedOrderStatusAsync(refund, cancellationToken);
        }
        else
        {
            _logger.LogDebug(
                "Refund {RefundId} for order {OrderId} still {Status} at Monobank",
                refund.Id, refund.OrderId, matchingEntry.Status);
        }
    }

    private async Task ApplyConfirmedRefundToOrderAsync(Refund refund, CancellationToken cancellationToken)
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

    private async Task RevertPrematureRefundedOrderStatusAsync(Refund refund, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == refund.OrderId, cancellationToken);

        if (order == null)
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

        var deliveredCount = await _context.Fulfillments
            .CountAsync(f => f.OrderId == order.Id, cancellationToken);

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
}
