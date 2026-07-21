using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.GetReconciliation;

public sealed class GetReconciliationQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetReconciliationQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetReconciliationResponse> HandleAsync(
        GetReconciliationQuery query,
        CancellationToken cancellationToken = default)
    {
        var totalOrders = await _context.Orders.CountAsync(cancellationToken);
        var fulfilledOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Fulfilled, cancellationToken);
        var pendingOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PendingFulfillment, cancellationToken);
        var partiallyFulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PartiallyFulfilled, cancellationToken);
        var cancelledOrders = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Refunded, cancellationToken);

        var totalRevenue = await _context.Orders
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .SumAsync(o => (long)o.Price, cancellationToken);

        var totalVouchers = await _context.FuelVouchers.CountAsync(cancellationToken);
        var availableVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Available, cancellationToken);
        var assignedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Assigned, cancellationToken);
        var usedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Used, cancellationToken);
        var failedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.VerificationFailed, cancellationToken);

        var unprocessedEvents = await _context.OutboxEvents.CountAsync(e => !e.Processed, cancellationToken);

        var recentImportErrors = await _context.VoucherImportErrors
            .CountAsync(e => e.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7), cancellationToken);

        var pendingOrderItems = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new GetReconciliationResponse.PendingOrderItem(
                o.Id,
                o.UserId,
                o.Provider,
                o.FuelTypeId,
                o.Liters,
                o.Quantity,
                o.Price,
                o.Status.ToString(),
                o.MonobankInvoiceId,
                o.MonobankStatus != null ? o.MonobankStatus.ToString() : null,
                o.CreatedAtUtc,
                o.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var voucherInventory = await _context.FuelVouchers
            .AsNoTracking()
            .GroupBy(v => new { v.Provider, v.FuelTypeId, v.Status })
            .Select(g => new GetReconciliationResponse.VoucherInventoryItem(
                g.Key.Provider,
                g.Key.FuelTypeId,
                g.Key.Status.ToString(),
                g.Count(),
                g.Sum(v => v.Liters)))
            .ToListAsync(cancellationToken);

        var monthlyRevenue = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month })
            .Select(g => new GetReconciliationResponse.MonthlyRevenueItem(
                g.Key.Year,
                g.Key.Month,
                string.Empty,
                g.Count(),
                g.Sum(o => (long)o.Price)))
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .ToListAsync(cancellationToken);

        var recentImports = await _context.VoucherImports
            .AsNoTracking()
            .OrderByDescending(i => i.StartedAtUtc)
            .Take(10)
            .Select(i => new GetReconciliationResponse.ImportSummaryItem(
                i.Id,
                i.FileName,
                i.Status,
                i.ImportedCount + i.DuplicateCount + i.FailedCount + i.VerificationFailedCount + i.VerifiedWithWarningsCount,
                i.FailedCount + i.VerificationFailedCount,
                i.VerifiedWithWarningsCount,
                i.VerificationFailedCount,
                i.StartedAtUtc))
            .ToListAsync(cancellationToken);

        var summary = new GetReconciliationResponse.SummaryData(
            totalOrders, fulfilledOrders, pendingOrders, partiallyFulfilled,
            cancelledOrders, totalRevenue, totalVouchers, availableVouchers,
            assignedVouchers, usedVouchers, failedVouchers, unprocessedEvents,
            recentImportErrors);

        return new GetReconciliationResponse(summary, pendingOrderItems, voucherInventory, monthlyRevenue, recentImports);
    }
}
