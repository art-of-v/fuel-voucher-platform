using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.GetSavingsReport;

/// <summary>
/// Builds the customer-facing savings summary for one user: what they paid, how many litres they
/// bought, how much they saved vs the pump (frozen at purchase), and how much they still hold
/// unredeemed. Scoped strictly to the caller's own orders. Never surfaces cost, margin or loss.
/// </summary>
public sealed class GetSavingsReportQueryHandler
{
    // Money was captured and not reversed: excludes never-paid (PendingPayment) and unwound
    // (Cancelled / fully Refunded) orders. Partial refunds are counted gross in v1 — same
    // simplification the operator P&L view (planning #86) makes.
    private static readonly OrderStatus[] PaidStatuses =
    [
        OrderStatus.Paid,
        OrderStatus.PendingFulfillment,
        OrderStatus.PartiallyFulfilled,
        OrderStatus.Fulfilled,
        OrderStatus.PartiallyRefunded
    ];

    private readonly ApplicationDbContext _context;

    public GetSavingsReportQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SavingsReportDto> HandleAsync(
        GetSavingsReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var fromDate = query.FromDate ?? DateTime.MinValue;
        var toDate = query.ToDate ?? DateTime.MaxValue;

        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Where(o => o.UserId == query.UserId
                && PaidStatuses.Contains(o.Status)
                && o.CreatedAtUtc >= fromDate
                && o.CreatedAtUtc <= toDate)
            .ToListAsync(cancellationToken);

        var lines = orders.SelectMany(o => o.LineItems).ToList();

        var totalPaid = lines.Sum(li => li.LineTotal);
        var totalLiters = lines.Sum(li => li.Liters * li.Quantity);

        // Only lines that captured a pump reference contribute a saving, and never a negative one
        // (a below-cost sale still saved the customer money vs the pump — it just cost us).
        static int LineSaving(OrderLineItem li) =>
            li.OriginalLineTotal.HasValue ? Math.Max(0, li.OriginalLineTotal.Value - li.LineTotal) : 0;

        var totalSavings = lines.Sum(LineSaving);

        // Per-month slice over the requested period, grouped by when the order was placed. Carries
        // the same three leak-free figures as the summary; oldest month first.
        var monthly = orders
            .GroupBy(o => o.CreatedAtUtc.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var monthLines = g.SelectMany(o => o.LineItems).ToList();
                return new MonthlySavings(
                    g.Key,
                    monthLines.Sum(li => li.LineTotal),
                    monthLines.Sum(LineSaving),
                    monthLines.Sum(li => li.Liters * li.Quantity));
            })
            .ToList();

        // Remaining = vouchers fulfilled to this user's orders that they still own and have not
        // redeemed at a station (Assigned). Used/Expired/Blocked/Deactivated all leave the pool.
        // This is a current "now" snapshot, deliberately NOT period-scoped — you still hold those
        // litres today regardless of when they were bought.
        var remainingOrderIds = await _context.Orders
            .AsNoTracking()
            .Where(o => o.UserId == query.UserId && PaidStatuses.Contains(o.Status))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var voucherIds = remainingOrderIds.Count == 0
            ? []
            : await _context.Fulfillments
                .AsNoTracking()
                .Where(f => remainingOrderIds.Contains(f.OrderId))
                .Select(f => f.VoucherId)
                .Distinct()
                .ToListAsync(cancellationToken);

        var remaining = voucherIds.Count == 0
            ? []
            : await _context.FuelVouchers
                .AsNoTracking()
                .Where(v => voucherIds.Contains(v.Id) && v.Status == VoucherStatus.Assigned)
                .Select(v => v.Liters)
                .ToListAsync(cancellationToken);

        return new SavingsReportDto
        {
            OrdersCount = orders.Count,
            TotalPaid = totalPaid,
            TotalLiters = totalLiters,
            TotalSavings = totalSavings,
            RemainingVouchers = remaining.Count,
            RemainingLiters = remaining.Sum(),
            Monthly = monthly
        };
    }
}
