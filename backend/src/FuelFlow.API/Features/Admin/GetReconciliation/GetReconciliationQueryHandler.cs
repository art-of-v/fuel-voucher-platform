using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
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

        var fuelPackages = await _context.FuelPackages
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var fpLookup = fuelPackages
            .GroupBy(fp => (fp.StationId, fp.FuelTypeId, fp.Liters))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(fp => fp.PriceUpdatedAt ?? fp.CreatedAtUtc).First());

        // Closed orders (incl. refunded) form the revenue set; margin is earned only on
        // delivered liters, so refunded liters drop out of revenue automatically.
        var fulfilledOrders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Where(o => o.Status == OrderStatus.Fulfilled
                || o.Status == OrderStatus.PartiallyFulfilled
                || o.Status == OrderStatus.PartiallyRefunded
                || o.Status == OrderStatus.Refunded)
            .ToListAsync(cancellationToken);

        long EarnedMarginKopecks(Order o)
        {
            long sum = 0;
            foreach (var g in o.LineItems.GroupBy(li => (li.Provider, li.FuelTypeId, li.Liters)))
            {
                if (!fpLookup.TryGetValue(g.Key, out var fp) || fp.MarginUahPerLiter is null)
                {
                    continue;
                }

                var delivered = o.Fulfillments.Count(f => f.Voucher is not null
                    && string.Equals(f.Voucher.Provider, g.Key.Provider, StringComparison.OrdinalIgnoreCase)
                    && f.Voucher.FuelTypeId == g.Key.FuelTypeId
                    && f.Voucher.Liters == g.Key.Liters);

                sum += (long)(fp.MarginUahPerLiter.Value * (decimal)g.Key.Liters * Math.Min(g.Sum(li => li.Quantity), delivered));
            }

            return sum;
        }

        var revenue = fulfilledOrders.Sum(EarnedMarginKopecks);

        var orphanVouchers = await _context.FuelVouchers
            .CountAsync(v => v.Status == VoucherStatus.Assigned
                && !_context.Fulfillments.Any(f => f.VoucherId == v.Id), cancellationToken);

        var unprocessed = await _context.OutboxEvents.CountAsync(e => !e.Processed, cancellationToken);

        var importErrors = await _context.VoucherImportErrors
            .CountAsync(e => e.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7), cancellationToken);

        var lowInventoryProviders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled)
            .SelectMany(o => o.LineItems.Select(li => li.Provider))
            .Distinct()
            .CountAsync(cancellationToken);

        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
            .Include(o => o.LineItems)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var allFulfillments = await _context.Fulfillments
            .Where(f => orderIds.Contains(f.OrderId))
            .ToListAsync(cancellationToken);
        var fulfillByOrder = allFulfillments.GroupBy(f => f.OrderId).ToDictionary(g => g.Key, g => g.Count());

        var refundsByOrder = await _context.Refunds
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .ToDictionaryAsync(r => r.OrderId, cancellationToken);

        var threeWayMatch = orders.Select(o =>
        {
            var lineItemsList = o.LineItems.ToList();
            var firstLi = lineItemsList.FirstOrDefault();
            var expected = lineItemsList.Sum(li => li.Quantity);
            var delivered = fulfillByOrder.GetValueOrDefault(o.Id, 0);
            var matchStatus = (o.Status == OrderStatus.Fulfilled && delivered >= expected) ? "OK"
                : o.Status == OrderStatus.Cancelled ? "CANCELLED"
                : o.Status == OrderStatus.Refunded ? "REFUNDED"
                : o.Status == OrderStatus.PartiallyRefunded ? "PARTIAL_REFUNDED"
                : (o.Status == OrderStatus.PendingPayment || o.Status == OrderStatus.Paid) ? "AWAITING_PAYMENT"
                : delivered == 0 ? "UNFULFILLED"
                : delivered < expected ? "PARTIAL"
                : "OK";

            refundsByOrder.TryGetValue(o.Id, out var refund);

            return new GetReconciliationResponse.ThreeWayMatchItem(
                o.Id,
                null,
                firstLi?.Provider ?? "",
                firstLi?.FuelTypeId ?? "",
                lineItemsList.Sum(li => li.Liters * li.Quantity),
                expected,
                o.Price,
                o.Status.ToString(),
                o.MonobankStatus?.ToString(),
                expected,
                delivered,
                matchStatus,
                (DateTime.UtcNow - o.CreatedAtUtc).Days,
                o.CreatedAtUtc,
                refund?.Status.ToString(),
                refund is { Status: RefundStatus.Completed } ? refund.Amount : 0L);
        }).ToList();

        var exceptions = new List<GetReconciliationResponse.ExceptionItem>();

        foreach (var o in threeWayMatch)
        {
            if (o.MatchStatus == "UNFULFILLED" && o.MonobankStatus == "Success")
                exceptions.Add(new(o.OrderId, "UNPAID_FULFILLMENT",
                    $"Order paid ({o.TotalPrice} UAH) but no vouchers assigned — {o.DaysSinceCreated}d old",
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

        var funnelRaw = await _context.FuelVouchers
            .AsNoTracking()
            .GroupBy(v => v.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), TotalLiters = g.Sum(v => v.Liters) })
            .ToListAsync(cancellationToken);

        var funnel = funnelRaw
            .Select(x => new GetReconciliationResponse.VoucherFunnelItem(
                x.Status.ToString(), x.Count, x.TotalLiters))
            .ToList();

        var revenueRaw = fulfilledOrders
            .GroupBy(o => new { o.CreatedAtUtc.Year, o.CreatedAtUtc.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                OrderCount = g.Count(),
                RevenueKopecks = g.Sum(EarnedMarginKopecks)
            })
            .ToList();

        var revenueSummary = revenueRaw
            .OrderByDescending(m => m.Year)
            .ThenByDescending(m => m.Month)
            .Select(x => new GetReconciliationResponse.RevenueSummaryItem(
                x.Year, x.Month, x.OrderCount, x.RevenueKopecks))
            .ToList();

        var summary = new GetReconciliationResponse.SummaryData(
            totalOrders, paidUnfulfilled, partiallyFulfilled, fulfilled,
            revenue, orphanVouchers, unprocessed, lowInventoryProviders, importErrors,
            orders.Count(o => o.Status == OrderStatus.PartiallyRefunded || o.Status == OrderStatus.Refunded),
            orders.Where(o => o.MonobankStatus == MonobankStatus.Success).Sum(o => Money.ToKopecks(o.Price)),
            orders.Sum(o => (long)RefundOrderCommandHandler.ComputeFulfilledValueKopecks(o)),
            refundsByOrder.Values.Where(r => r.Status == RefundStatus.Completed).Sum(r => (long)r.Amount));

        return new GetReconciliationResponse(summary, threeWayMatch, exceptions, funnel, revenueSummary);
    }
}
