using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicPackagesByBrand;

public sealed class GetPublicPackagesByBrandQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly PackageStockAvailability _stock;
    public GetPublicPackagesByBrandQueryHandler(ApplicationDbContext context, PackageStockAvailability stock)
    {
        _context = context;
        _stock = stock;
    }

    // Projects PublicPackageResponse (store-front fields only) — never the raw FuelPackage entity,
    // which carries the operator's supplier cost + margin. See PublicPackageResponse (planning #52).
    // OriginalPrice (the struck-through "before" price) is recomputed fresh from the pump price via
    // FuelPricing.OriginalPackagePrice (planning #73), so it is the public list/pump price — never the
    // leaked supplier cost — and every legacy row is corrected with no data migration. The SQL still
    // selects only store-front columns + pump (a public list number); cost/margin never leave the DB.
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesByBrandQuery query, CancellationToken ct = default)
    {
        // Resolved before the rows for the same reason as in GetPublicPackagesQueryHandler: which
        // packages exist depends on who is asking. The two routes must read the pool identically, or
        // whichever of them is laxer becomes the way into the other pool's catalogue.
        var isTestAccount = await _stock.IsTestAccountAsync(query.CallerUserId, ct);

        // Same rule as GetPublicPackagesQueryHandler, and for the same reason: this is the second anonymous
        // storefront route, so a filter on one of the two would leave the rehearsal package
        // purchasable through the other. Pool-aware there and here, because a filter that hid the QA
        // catalogue from everyone also hid it from QA.
        //
        // `data_kind` is on the offer, so the filter is on the offer; the fuel's display name is
        // joined from fuel_types for the same reason as there - the offer knows which fuel, not what
        // it is called.
var rows = await (
        // The fuel and its sizes; see GetPublicPackagesQueryHandler, which is the same query narrowed
        // to one Brand. No offer: the price is the fuel's own.
        from fuel in _context.FuelTypes.AsNoTracking()
        where !fuel.IsDeleted && fuel.BrandId == query.BrandId
        // The network's own code travels with the row: the stock keys below are matched on it, and
        // it is what the client renders next to the uuid it now orders by.
        join brand in _context.Brands.AsNoTracking() on fuel.BrandId equals brand.Id
        // The sizes belong to the fuel AND the pool - see GetPublicPackagesQueryHandler.
        from nominal in _context.FuelNominals.AsNoTracking()
        where nominal.FuelTypeId == fuel.Id
              && nominal.DataKind == DataKinds.ForPool(isTestAccount)
        orderby fuel.Name, nominal.Liters
        select new
        {
            fuel.BrandId,
            BrandSlug = brand.Slug,
            FuelTypeId = fuel.Id,
            FuelName = fuel.Name,
            nominal.DataKind,
            nominal.Liters,
            // Resolved against the fuel's own rate below - see GetPublicPackagesQueryHandler.
            fuel.SupplierPricePerLiter,
            fuel.MarginUahPerLiter,
            fuel.PumpPricePerLiter,
            fuel.MinDiscountPerLiter,
        })
        .ToListAsync(ct);

        var available = await _stock.AvailableKeysAsync(isTestAccount, ct);

        // A fuel nobody has priced yet is shown, not skipped: see the same rule and the same reason in
        // GetPublicPackagesQueryHandler - an empty shelf says "we do not sell this", and the column
        // price plus the stock badge say what is true.
        return rows
            .Select(x =>
            {
                // Our price when we can compute it; the column price when we cannot.
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
                    // A fuel we cannot price cannot be charged, so it is unavailable even with stock
                    // behind it: see the same rule in GetPublicPackagesQueryHandler.
                    available.Contains(StockKey.For(x.BrandSlug, x.FuelTypeId, x.Liters)) && ourPrice is not null);
            })
            .ToList();
    }
}
