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

        // One grouped query for the whole period: the totals and the monthly slice are the same
        // sum over the same lines, and the lines are the rows the database can group.
        var groups = await (
            from o in _context.Orders.AsNoTracking()
            join li in _context.OrderLineItems.AsNoTracking() on o.Id equals li.OrderId
            where o.UserId == query.UserId
                  && PaidStatuses.Contains(o.Status)
                  && o.CreatedAtUtc >= fromDate
                  && o.CreatedAtUtc <= toDate
                  // A company handover is not a customer payment, so it is nothing to save on. Real
                  // purchases AND renewals stay in: both are money the customer actually paid.
                  && o.Kind != OrderKind.ReceivedFromCompany
            group li by new { o.Id, o.CreatedAtUtc.Year, o.CreatedAtUtc.Month } into g
            select new
            {
                g.Key.Year,
                g.Key.Month,
                Paid = g.Sum(li => li.LineTotal),
                Liters = g.Sum(li => li.Liters * li.Quantity),
                // Only lines that captured a pump reference contribute a saving, and never a
                // negative one (a below-cost sale still saved the customer money vs the pump - it
                // just cost us). A line with no pump reference falls back to its own total, which
                // makes the difference zero - the same answer the in-process version gave.
                Savings = g.Sum(li => Math.Max(0m, (li.OriginalLineTotal ?? li.LineTotal) - li.LineTotal))
            })
            .ToListAsync(cancellationToken);

        var totalPaid = groups.Sum(g => g.Paid);
        var totalLiters = groups.Sum(g => g.Liters);
        var totalSavings = groups.Sum(g => g.Savings);

        // The orders are counted apart from their lines: an order that carries no line at all is
        // still an order the customer placed, and the count says so.
        var ordersCount = await _context.Orders
            .AsNoTracking()
            .CountAsync(o => o.UserId == query.UserId
                && PaidStatuses.Contains(o.Status)
                && o.CreatedAtUtc >= fromDate
                && o.CreatedAtUtc <= toDate
                && o.Kind != OrderKind.ReceivedFromCompany,
                cancellationToken);

        // Per-month slice over the requested period, grouped by when the order was placed. Carries
        // the same three leak-free figures as the summary; oldest month first.
        var monthly = groups
            .GroupBy(g => (g.Year, g.Month))
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlySavings(
                $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                g.Sum(x => x.Paid),
                g.Sum(x => x.Savings),
                g.Sum(x => x.Liters)))
            .ToList();

        // Remaining = vouchers fulfilled to this user's orders that they still own and have not
        // redeemed at a Brand (Assigned). Used/Expired/Blocked/Deactivated all leave the pool.
        // This is a current "now" snapshot, deliberately NOT period-scoped - you still hold those
        // litres today regardless of when they were bought.
        //
        // One query for the whole thing: the order ids, the voucher ids and the litres were three
        // round trips with a growing IN-list between them.
        var remaining = await (
            from o in _context.Orders.AsNoTracking()
            join f in _context.Fulfillments.AsNoTracking() on o.Id equals f.OrderId
            join v in _context.FuelVouchers.AsNoTracking() on f.VoucherId equals v.Id
            where o.UserId == query.UserId
                  && PaidStatuses.Contains(o.Status)
                  // A voucher the company handed over was never paid for by this user, so it must
                  // not inflate what they "still hold" either - same rule as the spend figures above.
                  && o.Kind != OrderKind.ReceivedFromCompany
                  && v.Status == VoucherStatus.Assigned
            // Grouped by the voucher, so a voucher with two fulfilments is counted once - the
            // distinct voucher ids the old query asked for.
            group v by v.Id into g
            select new { Liters = g.Sum(x => x.Liters) })
            .ToListAsync(cancellationToken);

        return new SavingsReportDto
        {
            OrdersCount = ordersCount,
            TotalPaid = totalPaid,
            TotalLiters = totalLiters,
            TotalSavings = totalSavings,
            RemainingVouchers = remaining.Count,
            RemainingLiters = remaining.Sum(r => r.Liters),
            Monthly = monthly
        };
    }
}
