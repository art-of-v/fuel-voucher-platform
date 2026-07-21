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
        var fulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Fulfilled, cancellationToken);
        var paidUnfulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PendingFulfillment, cancellationToken);
        var partiallyFulfilled = await _context.Orders.CountAsync(o => o.Status == OrderStatus.PartiallyFulfilled, cancellationToken);

        var revenue = await _context.Orders
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .SumAsync(o => (long)o.Price, cancellationToken);

        var orphanVouchers = await _context.FuelVouchers
            .CountAsync(v => v.Status == VoucherStatus.Assigned
                && !_context.Fulfillments.Any(f => f.VoucherId == v.Id), cancellationToken);

        var unprocessed = await _context.OutboxEvents.CountAsync(e => !e.Processed, cancellationToken);

        var importErrors = await _context.VoucherImportErrors
            .CountAsync(e => e.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7), cancellationToken);

        var lowInventoryProviders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
            .Select(o => o.Provider)
            .Distinct()
            .CountAsync(cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Fulfillments)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var allFulfillments = await _context.Fulfillments
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);
        var fulfillByOrder = allFulfillments.GroupBy(f => f.OrderId).ToDictionary(g => g.Key, g => g.Count());

        var threeWayMatch = orders.Select(o =>
        {
            var expected = o.Quantity;
            var delivered = fulfillByOrder.GetValueOrDefault(o.Id, 0);
            var matchStatus = (o.Status == OrderStatus.Fulfilled && delivered >= expected) ? "OK"
                : (o.Status == OrderStatus.Cancelled || o.Status == OrderStatus.Refunded) ? "CANCELLED"
                : (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Paid) ? "AWAITING_PAYMENT"
                : delivered == 0 ? "UNFULFILLED"
                : delivered < expected ? "PARTIAL"
                : "OK";

            return new GetReconciliationResponse.ThreeWayMatchItem(
                o.Id,
                null,
                o.Provider,
                o.FuelTypeId,
                o.Liters,
                o.Quantity,
                o.Price,
                o.Status.ToString(),
                o.MonobankStatus?.ToString(),
                expected,
                delivered,
                matchStatus,
                (DateTime.UtcNow - o.CreatedAtUtc).Days,
                o.CreatedAtUtc);
        }).ToList();

        var exceptions = new List<GetReconciliationResponse.ExceptionItem>();

        foreach (var o in threeWayMatch)
        {
            if (o.MatchStatus == "UNFULFILLED" && o.MonobankStatus == "Success")
                exceptions.Add(new(o.OrderId, "UNPAID_FULFILLMENT",
                    $"Order paid ({(o.TotalPrice / 100m):F2} UAH) but no vouchers assigned — {o.DaysSinceCreated}d old",
                    "critical", o.CreatedAtUtc));
            else if (o.MatchStatus == "UNFULFILLED")
                exceptions.Add(new(o.OrderId, "UNFULFILLED",
                    $"{o.Provider} {o.FuelType} {o.Liters}L×{o.Quantity} — no inventory ({o.DaysSinceCreated}d waiting)",
                    "warning", o.CreatedAtUtc));
            else if (o.MatchStatus == "PARTIAL")
                exceptions.Add(new(o.OrderId, "PARTIAL",
                    $"Only {o.VouchersDelivered}/{o.VouchersExpected} vouchers delivered for {o.Provider} {o.FuelType}",
                    "warning", o.CreatedAtUtc));
        }

        if (orphanVouchers > 0)
            exceptions.Add(new(Guid.Empty, "ORPHAN",
                $"{orphanVouchers} voucher(s) marked Assigned but linked to no fulfillment record",
                "critical", DateTime.UtcNow));

        if (unprocessed > 10)
            exceptions.Add(new(Guid.Empty, "OUTBOX",
                $"{unprocessed} unprocessed outbox events — fulfillment jobs may be stalled",
                "critical", DateTime.UtcNow));

        var funnel = await _context.FuelVouchers
            .AsNoTracking()
            .GroupBy(v => v.Status)
            .Select(g => new GetReconciliationResponse.VoucherFunnelItem(
                g.Key.ToString(),
                g.Count(),
                g.Sum(v => v.Liters)))
            .ToListAsync(cancellationToken);

        var revenueSummary = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
            .GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month })
            .Select(g => new GetReconciliationResponse.RevenueSummaryItem(
                g.Key.Year,
                g.Key.Month,
                g.Count(),
                g.Sum(o => (long)o.Price)))
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .ToListAsync(cancellationToken);

        var summary = new GetReconciliationResponse.SummaryData(
            totalOrders, paidUnfulfilled, partiallyFulfilled, fulfilled,
            revenue, orphanVouchers, unprocessed, lowInventoryProviders, importErrors);

        return new GetReconciliationResponse(summary, threeWayMatch, exceptions, funnel, revenueSummary);
    }
}
