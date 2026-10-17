using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackages;

public sealed class GetPublicPackagesQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly PackageStockAvailability _stock;
    public GetPublicPackagesQueryHandler(ApplicationDbContext context, PackageStockAvailability stock)
    {
        _context = context;
        _stock = stock;
    }

    // Projects PublicPackageResponse (store-front fields only) — never the raw offer/volume entities,
    // which carry the operator's supplier cost + margin. See PublicPackageResponse (planning #52).
    // OriginalPrice (the struck-through "before" price) is recomputed fresh from the pump price via
    // FuelPricing.OriginalPackagePrice (planning #73), so it is the public list/pump price — never the
    // leaked supplier cost — and every legacy row is corrected with no data migration. The SQL still
    // selects only store-front columns + pump (a public list number); cost/margin never leave the DB.
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesQuery query, CancellationToken ct = default)
    {
        // Read the caller's pool before the rows, not after: which packages exist is a question about
        // who is asking, and asking it afterwards only works if the answer was constant. This is the
        // same resolution the stock side uses, deliberately - see PackageStockAvailability.
        //
        // Resolved from the database rather than the token, and only when a token was actually sent:
        // an anonymous catalogue read is the common case (marketing site, pre-login browsing) and must
        // not pay for a users lookup. A missing or soft-deleted row falls back to the real pool, which
        // is the safe direction - the caller then sees exactly what the public storefront shows.
var isTestAccount = await _stock.IsTestAccountAsync(query.CallerUserId, ct);

        // Rehearsal packages are real rows and stay visible to the admin screens, but this endpoint is
        // what the app and the marketing site read, and a test package reaching a non-QA caller is
        // purchasable - which is exactly how "QA-CHECK-95", 5 L at 1 UAH, ended up on sale in
        // production. Filtered here rather than by a global query filter so the admin can still see and
        // fix the row.
        //
        // The predicate is pool-aware rather than a flat "data_kind = 'real'" because hiding them from
        // everyone also hid them from the one caller they exist for. QA fixtures had no choice but to
        // reuse the live catalogue, which meant a test run exercised real package ids against real
        // prices. A QA caller now sees the QA pool and nobody else sees it at all.
        //
        // Query syntax rather than chained `Join` calls: the second join keys off the offer, which is not in
        // scope after an anonymous-typed first join. The joins are explicit because the offer carries
        // no navigation to FuelType - station and fuel are natural keys throughout this schema, so there
        // is nothing to navigate; the fuel's display name is joined for here and nowhere else.
        var rows = await (
            from volume in _context.FuelOfferVolumes.AsNoTracking()
            join offer in _context.FuelOffers.AsNoTracking() on volume.OfferId equals offer.Id
            join fuel in _context.FuelTypes.AsNoTracking() on offer.FuelTypeId equals fuel.Id
            // `data_kind` lives on the offer, so the filter is on the offer rather than on the volume:
            // filtering `volume.DataKind` would silently read as always-true and the pool guard would
            // evaporate.
            where offer.DataKind == DataKinds.ForPool(isTestAccount)
            orderby offer.StationId, fuel.Name, volume.Liters
            // Fuel name moved to fuel_types with the rest of the fuel's identity when the offer was
            // born - the offer knows WHICH fuel, not what it is called. Joining for the label keeps the
            // storefront's display name a property of the fuel rather than a string each package row
            // could contradict.
            select new
            {
                Id = volume.Id,
                offer.StationId,
                offer.FuelTypeId,
                FuelName = fuel.Name,
                volume.Liters,
                // The nominal's own price if one was set, otherwise null — resolved below against the
                // offer's rate. Four nullable columns cross the wire to the projection; the supplier cost
                // never leaves the database.
                volume.PriceOverrideUah,
                offer.SupplierPricePerLiter,
                offer.MarginUahPerLiter,
                offer.PumpPricePerLiter,
                offer.MinDiscountPerLiter,
            })
            .ToListAsync(ct);

        // One grouped query for the whole catalog rather than an existence probe per package: this
        // endpoint feeds the map radar as well as the shop, and the count is small enough to hold in
        // memory but far too large to ask about N times. Same pool as the row filter above - the two
        // must agree, or a package would advertise itself with another pool's stock behind it.
        var available = await _stock.AvailableKeysAsync(isTestAccount, ct);

        return rows
            // A package whose fuel nobody has priced yet and which carries no override has no price to
            // show. It is skipped rather than published at zero: a zero on the shelf is a free fuel, and
            // the row would also be unbuyable. On production that is 0 of 122 rows.
            .Where(x => x.PriceOverrideUah is not null
                || (x.SupplierPricePerLiter is not null && x.MarginUahPerLiter is not null))
            .Select(x =>
            {
                var price = FuelPricing.PackagePrice(
                    x.SupplierPricePerLiter, x.MarginUahPerLiter, x.PumpPricePerLiter, x.MinDiscountPerLiter,
                    x.Liters, x.PriceOverrideUah);

                return new PublicPackageResponse(
                    x.Id,
                    x.StationId,
                    x.FuelTypeId,
                    x.FuelName,
                    x.Liters,
                    price,
                    FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, price),
                    FuelPricing.FinalPerLiterOrNull(
                        x.SupplierPricePerLiter, x.MarginUahPerLiter, x.PumpPricePerLiter, x.MinDiscountPerLiter),
                    available.Contains(StockKey.For(x.StationId, x.FuelTypeId, x.Liters)));
            })
            .ToList();
    }
}
