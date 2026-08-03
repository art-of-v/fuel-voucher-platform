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

        if (matchingEntry.Status.Equals("success", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Completed;
            refund.MonobankStatus = "success";
            refund.ErrorMessage = null;
            refund.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Refund {RefundId} for order {OrderId} confirmed by Monobank ({Amount} kopecks)",
                refund.Id, refund.OrderId, refund.Amount);
        }
        else if (matchingEntry.Status.Equals("failure", StringComparison.OrdinalIgnoreCase))
        {
            refund.Status = RefundStatus.Failed;
            refund.MonobankStatus = "failure";
            refund.ErrorMessage = "Monobank reported refund failure";
            refund.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogWarning(
                "Refund {RefundId} for order {OrderId} failed per Monobank",
                refund.Id, refund.OrderId);
        }
        else
        {
            _logger.LogDebug(
                "Refund {RefundId} for order {OrderId} still {Status} at Monobank",
                refund.Id, refund.OrderId, matchingEntry.Status);
        }
    }
}
