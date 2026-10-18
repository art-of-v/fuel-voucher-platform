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
            from volume in _context.FuelOfferVolumes.AsNoTracking()
            join offer in _context.FuelOffers.AsNoTracking() on volume.OfferId equals offer.Id
            join fuel in _context.FuelTypes.AsNoTracking() on offer.FuelTypeId equals fuel.Id
            where offer.BrandId == query.BrandId
               && offer.DataKind == DataKinds.ForPool(isTestAccount)
            orderby fuel.Name, volume.Liters
            select new
            {
                Id = volume.Id,
                offer.BrandId,
                offer.FuelTypeId,
                FuelName = fuel.Name,
                volume.Liters,
                // Resolved against the offer's rate below — see GetPublicPackagesQueryHandler, which is
                // the same query narrowed to one Brand.
                volume.PriceOverrideUah,
                offer.SupplierPricePerLiter,
                fuel.MarginUahPerLiter,
                offer.PumpPricePerLiter,
                offer.MinDiscountPerLiter,
            })
            .ToListAsync(ct);

        var available = await _stock.AvailableKeysAsync(isTestAccount, ct);

        // Unpriced, unoverridden packages are skipped rather than shown at zero: see the same filter in
        // GetPublicPackagesQueryHandler.
        return rows
            .Where(x => x.PriceOverrideUah is not null
                || (x.SupplierPricePerLiter is not null && x.MarginUahPerLiter is not null))
            .Select(x =>
            {
                var price = FuelPricing.PackagePrice(
                    x.SupplierPricePerLiter, x.MarginUahPerLiter, x.PumpPricePerLiter, x.MinDiscountPerLiter,
                    x.Liters, x.PriceOverrideUah);

                return new PublicPackageResponse(
                    x.Id,
                    x.BrandId,
                    x.FuelTypeId,
                    x.FuelName,
                    x.Liters,
                    price,
                    FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, price),
                    FuelPricing.FinalPerLiterOrNull(
                        x.SupplierPricePerLiter, x.MarginUahPerLiter, x.PumpPricePerLiter, x.MinDiscountPerLiter),
                    available.Contains(StockKey.For(x.BrandId, x.FuelTypeId, x.Liters)));
            })
            .ToList();
    }
}
