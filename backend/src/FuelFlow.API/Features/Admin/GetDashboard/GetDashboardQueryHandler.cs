using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Admin.GetDashboard;

public sealed class GetDashboardQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetDashboardQueryHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GetDashboardResponse> HandleAsync(
        GetDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        var totalUsers = await _context.Users.CountAsync(cancellationToken);
        var totalVouchers = await _context.FuelVouchers.CountAsync(cancellationToken);
        var availableVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Available || v.Status == VoucherStatus.Imported, cancellationToken);
        var assignedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Assigned, cancellationToken);
        var pendingReconciliationVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.PendingReconciliation, cancellationToken);
        var usedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Used, cancellationToken);
        var decodeIntegrityFailedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.DecodeIntegrityFailed, cancellationToken);
        var decodeIntegrityWarningVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.DecodeIntegrityWarning, cancellationToken);

        // A company handing already-purchased fuel to a worker is not a sale — it would inflate
        // the order counters and, worse, book the handover as revenue the platform never earned.
        var totalOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany, cancellationToken);
        var pendingOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany && (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled), cancellationToken);
        var fulfilledOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany && o.Status == OrderStatus.Fulfilled, cancellationToken);

        // The profit, summed by the database. This used to load every fulfilled order and every one
        // of its line items into the process to add them up - a read that grows with every sale, for
        // a number that is one SUM over a join.
        //
        // The margin lives on the FUEL TYPE, and the size is a row in fuel_nominals - whose key
        // (fuel_type_id, data_kind, litres) is one row per fuel, pool and size - so a line can match
        // at most one margin. Pinned to the real pool, like the counters above: a QA rehearsal row
        // must not shadow a real one.
        //
        // The line names the brand and the fuel type belongs to one. A line whose brand and fuel
        // type disagree has no margin - the lookup this replaces said the same, and answered zero.
        //
        // The per-line truncation is the shipped behaviour and is kept: each line's contribution is
        // truncated to whole kopecks before it is added, so the total is the sum of the truncated
        // lines and not the truncation of the total. Math.Truncate is trunc() in SQL, which is what
        // keeps that true - a cast to an integer type would ROUND in Postgres.
        var profitLines =
            from o in _context.Orders.AsNoTracking()
            join li in _context.OrderLineItems.AsNoTracking() on o.Id equals li.OrderId
            join f in _context.FuelTypes.AsNoTracking() on li.FuelTypeId equals f.Id
            join b in _context.Brands.AsNoTracking() on f.BrandId equals b.Id
            where (o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
                // Same reasoning as the counters above: a handover carries no revenue for us.
                && o.Kind != OrderKind.ReceivedFromCompany
                && b.Slug == li.Brand
                // The size has to exist in the real pool: the lookup this replaces only contained the
                // (fuel, litres) pairs that have a real nominal, so a line without one had no margin.
                && _context.FuelNominals.Any(n => n.FuelTypeId == f.Id && n.Liters == li.Liters && n.DataKind == DataKinds.Real)
            select Math.Truncate((f.MarginUahPerLiter ?? 0m) * li.Liters * li.Quantity);

        var profitKopecks = (long)await profitLines.SumAsync(cancellationToken);
        var revenueUah = profitKopecks;

        var byProvider = await _context.FuelVouchers
            .GroupBy(v => v.Brand)
            .Select(g => new GetDashboardResponse.VouchersByProvider(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new GetDashboardResponse(
            new GetDashboardResponse.UsersStats(totalUsers),
            new GetDashboardResponse.VouchersStats(totalVouchers, availableVouchers, assignedVouchers, pendingReconciliationVouchers, usedVouchers, decodeIntegrityFailedVouchers, decodeIntegrityWarningVouchers, byProvider),
            new GetDashboardResponse.OrdersStats(totalOrders, pendingOrders, fulfilledOrders, revenueUah));
    }
}
