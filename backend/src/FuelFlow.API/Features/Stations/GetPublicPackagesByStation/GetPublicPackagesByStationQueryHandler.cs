using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackagesByStation;

public sealed class GetPublicPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicPackagesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    // Projects PublicPackageResponse (store-front fields only) — never the raw FuelPackage entity,
    // which carries the operator's supplier cost + margin. See PublicPackageResponse (planning #52).
    // OriginalPrice (the struck-through "before" price) is recomputed fresh from the pump price via
    // FuelPricing.OriginalPackagePrice (planning #73), so it is the public list/pump price — never the
    // leaked supplier cost — and every legacy row is corrected with no data migration. The SQL still
    // selects only store-front columns + pump (a public list number); cost/margin never leave the DB.
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesByStationQuery query, CancellationToken ct = default)
    {
        var rows = await _context.FuelPackages
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
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

        return rows
            .Select(x => new PublicPackageResponse(
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                FuelPricing.OriginalPackagePrice(x.PumpPricePerLiter, x.Liters, x.Price),
                x.FinalPricePerLiter))
            .ToList();
    }
}
