using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.API.BackgroundJobs;

public class RefundStatusSyncService
{
    private readonly ApplicationDbContext _context;
    private readonly IMonobankClient _monobankClient;
    private readonly ILogger<RefundStatusSyncService> _logger;

    public RefundStatusSyncService(
        ApplicationDbContext context,
        IMonobankClient monobankClient,
        ILogger<RefundStatusSyncService> logger)
    {
        _context = context;
        _monobankClient = monobankClient;
        _logger = logger;
    }

    public virtual async Task SyncPendingRefundsAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(24);
        var staleRefunds = await _context.Refunds
            .Where(r => r.Status == RefundStatus.Processing && r.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (staleRefunds.Count > 0)
        {
            foreach (var staleRefund in staleRefunds)
            {
                staleRefund.Status = RefundStatus.Failed;
                staleRefund.ErrorMessage = "Timed out waiting for Monobank confirmation (refund abandoned)";
                staleRefund.UpdatedAtUtc = DateTime.UtcNow;
                _logger.LogWarning(
                    "Failed refund {RefundId} (invoice {InvoiceId}) timed out after 24h; status reset to Failed",
                    staleRefund.Id, staleRefund.InvoiceId);
            }
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Marked {Count} stale refunds as Failed", staleRefunds.Count);
        }

        var pendingRefunds = await _context.Refunds
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

        foreach (var refund in pendingRefunds)
        {
            try
            {
                var status = await _monobankClient.GetInvoiceStatusAsync(refund.InvoiceId, cancellationToken);
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
        var matchingEntry = cancelList?
            .OrderByDescending(e => e.ModifiedDate)
            .FirstOrDefault(e => e.ExtRef == refund.ExtRef);

        matchingEntry ??= cancelList?
            .OrderByDescending(e => e.ModifiedDate)
            .FirstOrDefault();

        if (matchingEntry == null)
        {
            return;
        }

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == refund.OrderId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found for refund {RefundId}", refund.OrderId, refund.Id);
            return;
        }

        if (matchingEntry.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Completed;
            refund.MonobankStatus = "success";
            refund.ErrorMessage = null;
            refund.UpdatedAtUtc = DateTime.UtcNow;

            // Update order status based on fulfillment state
            var deliveredCount = await _context.Fulfillments
                .CountAsync(f => f.OrderId == order.Id, cancellationToken);

            var newStatus = deliveredCount > 0
                ? OrderStatus.PartiallyRefunded
                : OrderStatus.Refunded;

            ApplyOrderStatus(order, newStatus);

            _logger.LogInformation(
                "Refund {RefundId} for order {OrderId} confirmed by Monobank ({Amount} kopecks). Order status updated to {Status}",
                refund.Id, refund.OrderId, refund.Amount, newStatus);
        }
        else if (matchingEntry.Status.Equals("failure", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Failed;
            refund.MonobankStatus = "failure";
            refund.ErrorMessage = "Monobank reported refund failure";
            refund.UpdatedAtUtc = DateTime.UtcNow;

            // On failure, revert order to PartiallyFulfilled so refund can be retried
            if (order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
            {
                ApplyOrderStatus(order, OrderStatus.PartiallyFulfilled);
            }

            _logger.LogWarning(
                "Refund {RefundId} for order {OrderId} failed per Monobank. Order status reverted to PartiallyFulfilled",
                refund.Id, refund.OrderId);
        }
        else
        {
            _logger.LogDebug(
                "Refund {RefundId} for order {OrderId} still {Status} at Monobank",
                refund.Id, refund.OrderId, matchingEntry.Status);
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
}
