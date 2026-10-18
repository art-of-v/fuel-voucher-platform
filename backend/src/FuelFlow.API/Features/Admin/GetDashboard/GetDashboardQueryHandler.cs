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
        var usedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.Used, cancellationToken);
        var verificationFailedVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.VerificationFailed, cancellationToken);
        var verifiedWithWarningsVouchers = await _context.FuelVouchers.CountAsync(v => v.Status == VoucherStatus.VerifiedWithWarnings, cancellationToken);

        // A company handing already-purchased fuel to a worker is not a sale — it would inflate
        // the order counters and, worse, book the handover as revenue the platform never earned.
        var totalOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany, cancellationToken);
        var pendingOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany && (o.Status == OrderStatus.PendingFulfillment || o.Status == OrderStatus.PartiallyFulfilled), cancellationToken);
        var fulfilledOrders = await _context.Orders.CountAsync(o => o.Kind != OrderKind.ReceivedFromCompany && o.Status == OrderStatus.Fulfilled, cancellationToken);

        // Margin is a property of the offer, so the lookup is volumes joined to offers, pinned to the
        // real pool: these are operator counters over real business, and the old lookup had no pool
        // filter, so a QA rehearsal row could shadow a real one by being newer.
        //
        // The GroupBy(...).First() this replaces existed only to break ties the schema already
        // forbids - UNIQUE (station_id, fuel_type_id, data_kind) on fuel_offers times
        // UNIQUE (offer_id, liters) on fuel_offer_volumes makes the triple unique inside a pool. A
        // duplicate now throws rather than silently resolving to one arbitrary price.
        // Projects the margin, not the offer entity. Handing back a whole tracked-free entity and then
        // reading one column off it loads every offer column for every volume in the catalogue to
        // answer one question, and - worse - depends on the provider materialising the navigation,
        // which the in-memory provider does not and which would make this throw on a null rather than
        // return a wrong number.
        // An explicit join rather than `v.Offer.X`: relying on the navigation being materialised is a
        // silent dependency on provider behaviour, and where it is not materialised this throws on a
        // null instead of returning a wrong number - which is exactly the kind of failure that looks
        // like a data problem and gets debugged as one.
        //
        // Projects the margin alone. Handing back the offer entity would load every offer column for
        // every volume in the catalogue to answer one question.
        var fpLookup = await (
            from v in _context.FuelOfferVolumes.AsNoTracking()
            join o in _context.FuelOffers.AsNoTracking() on v.OfferId equals o.Id
            // The margin is the fuel's, so the fuel joins too. Handing back the offer entity would load
            // every offer column for every volume in the catalogue to answer one question.
            join f in _context.FuelTypes.AsNoTracking() on o.FuelTypeId equals f.Id
            where o.DataKind == DataKinds.Real
            select new { o.StationId, o.FuelTypeId, v.Liters, MarginUahPerLiter = f.MarginUahPerLiter })
            .ToDictionaryAsync(
                x => (x.StationId, x.FuelTypeId, x.Liters),
                x => x.MarginUahPerLiter,
                cancellationToken);

        var fulfilledOrdersList = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Where(o => (o.Status == OrderStatus.Fulfilled || o.Status == OrderStatus.PartiallyFulfilled)
                // Same reasoning as the counters above: a handover carries no revenue for us.
                && o.Kind != OrderKind.ReceivedFromCompany)
            .ToListAsync(cancellationToken);

        var profitKopecks = fulfilledOrdersList.Sum(o =>
            o.LineItems.Sum(li =>
            {
                if (fpLookup.TryGetValue((li.Brand, li.FuelTypeId, li.Liters), out var margin))
                    return (long)((margin ?? 0) * (decimal)li.Liters * li.Quantity);
                return 0L;
            })
        );

        var revenueUah = profitKopecks;

        var byProvider = await _context.FuelVouchers
            .GroupBy(v => v.Brand)
            .Select(g => new GetDashboardResponse.VouchersByProvider(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return new GetDashboardResponse(
            new GetDashboardResponse.UsersStats(totalUsers),
            new GetDashboardResponse.VouchersStats(totalVouchers, availableVouchers, assignedVouchers, usedVouchers, verificationFailedVouchers, verifiedWithWarningsVouchers, byProvider),
            new GetDashboardResponse.OrdersStats(totalOrders, pendingOrders, fulfilledOrders, revenueUah));
    }
}
