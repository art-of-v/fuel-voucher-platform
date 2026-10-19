using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicPackages;

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
// Query syntax rather than chained `Join` calls: the nominal join keys off the fuel, which is not
    // in scope before the offer is joined. The joins are explicit because the offer carries no
    // navigation to FuelType - Brand and fuel are natural keys throughout this schema, so there is
    // nothing to navigate; the fuel's display name is joined for here and nowhere else.
    var rows = await (
// The fuel and its sizes. There is no offer in this query and there will not be: the price is the
    // fuel's own, so a second row saying what a litre costs could only ever disagree with the first.
    // The pool lives on the nominal, which is the only thing left that varies.
    from fuel in _context.FuelTypes.AsNoTracking()
    where !fuel.IsDeleted
    // The network's own code travels with the row: the stock keys below are matched on it, and it is
    // what the client renders next to the uuid it now orders by.
    join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
    // The sizes belong to the fuel AND the pool: a nominal row says the operator allows this size in
    // this catalogue, so a 7 L canister that exists only to rehearse a pickup is not a size the
    // public site lists.
    from nominal in _context.FuelNominals.AsNoTracking()
    where nominal.FuelTypeId == fuel.Id
          && nominal.DataKind == DataKinds.ForPool(isTestAccount)
    orderby fuel.BrandId, fuel.Name, nominal.Liters
    select new
    {
        fuel.BrandId,
        BrandSlug = brand.Slug,
        FuelTypeId = fuel.Id,
        FuelName = fuel.Name,
        nominal.DataKind,
        nominal.Liters,
        // The supplier cost never leaves the database: it is selected, priced with, and dropped.
        fuel.SupplierPricePerLiter,
        fuel.MarginUahPerLiter,
        fuel.PumpPricePerLiter,
        fuel.MinDiscountPerLiter,
    })
    .ToListAsync(ct);

    // One grouped query for the whole catalog rather than an existence probe per package: this
    // endpoint feeds the map radar as well as the shop, and the count is small enough to hold in
    // memory but far too large to ask about N times. Same pool as the row filter above - the two
    // must agree, or a package would advertise itself with another pool's stock behind it.
        var available = await _stock.AvailableKeysAsync(isTestAccount, ct);

    return rows
    // A fuel nobody has priced yet is NOT skipped. It used to be, on the grounds that "a zero on the
    // shelf is a free fuel" - but skipping it makes the shelf EMPTY, and an empty shelf does not say
    // "we have not costed this yet", it says "we do not sell this". The catalogue import creates
    // fuels with the published pump price and no cost, and those fuels are exactly the ones a
    // customer is looking for. The price shown is the column price the market publishes, and the
    // stock badge says it cannot be bought - the checkout still refuses it, with a 400, because there
    // is no cost to charge against.
    .Select(x =>
    {
    // Our price when we can compute it; the column price when we cannot. The two are the same
    // number once a cost is known, because the formula caps our price at the pump price anyway.
    var ourPrice = FuelPricing.FinalPerLiterOrNull(
        x.SupplierPricePerLiter, x.MarginUahPerLiter, x.PumpPricePerLiter, x.MinDiscountPerLiter);
    var perLiter = ourPrice ?? x.PumpPricePerLiter ?? 0m;
    var price = Math.Round(perLiter * x.Liters, 2, MidpointRounding.AwayFromZero);

    return new PublicPackageResponse(
                    PackageIds.For(x.FuelTypeId, x.DataKind, x.Liters),
                    x.BrandId,
                    x.BrandSlug,
                    x.FuelTypeId,
                    x.FuelName,
                    x.Liters,
                    price,
                    FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, price),
                    perLiter,
                    // "Can this be bought" is not the same question as "is there stock". A fuel we
                    // cannot price cannot be charged - the checkout refuses it with a 400 - so it is
                    // marked unavailable even when vouchers are behind it, which is the only way the
                    // badge and the checkout agree.
                    available.Contains(StockKey.For(x.BrandSlug, x.FuelTypeId, x.Liters)) && ourPrice is not null);
            })
            .ToList();
    }
}
