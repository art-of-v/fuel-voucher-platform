using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackagesByStation;

public sealed class GetPublicPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly PackageStockAvailability _stock;
    public GetPublicPackagesByStationQueryHandler(ApplicationDbContext context, PackageStockAvailability stock)
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
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesByStationQuery query, CancellationToken ct = default)
    {
        // Resolved before the rows for the same reason as in GetPublicPackagesQueryHandler: which
        // packages exist depends on who is asking. The two routes must read the pool identically, or
        // whichever of them is laxer becomes the way into the other pool's catalogue.
        var isTestAccount = await _stock.IsTestAccountAsync(query.CallerUserId, ct);

        var rows = await _context.FuelPackages
            .AsNoTracking()
            // Same rule as GetPublicPackagesQueryHandler, and for the same reason: this is the
            // second anonymous storefront route, so a filter on one of the two would leave the
            // rehearsal package purchasable through the other. Pool-aware there and here, because a
            // filter that hid the QA catalogue from everyone also hid it from QA.
            .Where(x => x.StationId == query.StationId && x.IsTestData == isTestAccount)
            .OrderBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .Select(x => new
            {
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                x.PumpPricePerLiter,
                x.FinalPricePerLiter,
            })
            .ToListAsync(ct);

        var available = await _stock.AvailableKeysAsync(isTestAccount, ct);

        return rows
            .Select(x => new PublicPackageResponse(
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, x.Price),
                x.FinalPricePerLiter,
                available.Contains(StockKey.For(x.StationId, x.FuelTypeId, x.Liters))))
            .ToList();
    }
}
