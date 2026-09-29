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
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Where(o => o.UserId == query.UserId && PaidStatuses.Contains(o.Status))
            .ToListAsync(cancellationToken);

        var lines = orders.SelectMany(o => o.LineItems).ToList();

        var totalPaid = lines.Sum(li => li.LineTotal);
        var totalLiters = lines.Sum(li => li.Liters * li.Quantity);

        // Only lines that captured a pump reference contribute a saving, and never a negative one
        // (a below-cost sale still saved the customer money vs the pump — it just cost us).
        var totalSavings = lines
            .Where(li => li.OriginalLineTotal.HasValue)
            .Sum(li => Math.Max(0, li.OriginalLineTotal!.Value - li.LineTotal));

        // Remaining = vouchers fulfilled to this user's orders that they still own and have not
        // redeemed at a station (Assigned). Used/Expired/Blocked/Deactivated all leave the pool.
        var orderIds = orders.Select(o => o.Id).ToList();

        var voucherIds = await _context.Fulfillments
            .AsNoTracking()
            .Where(f => orderIds.Contains(f.OrderId))
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
            RemainingLiters = remaining.Sum()
        };
    }
}
