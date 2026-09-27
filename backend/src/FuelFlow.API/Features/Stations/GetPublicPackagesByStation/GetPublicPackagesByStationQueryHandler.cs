using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackagesByStation;

public sealed class GetPublicPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicPackagesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    // Projects PublicPackageResponse (store-front fields only) — never the raw FuelPackage entity,
    // which carries the operator's supplier cost + margin. See PublicPackageResponse (planning #52).
    public async Task<List<PublicPackageResponse>> HandleAsync(GetPublicPackagesByStationQuery query, CancellationToken ct = default) =>
        await _context.FuelPackages
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
            .OrderBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .Select(x => new PublicPackageResponse(
                x.Id,
                x.StationId,
                x.FuelTypeId,
                x.FuelName,
                x.Liters,
                x.Price,
                x.OriginalPrice,
                x.FinalPricePerLiter))
            .ToListAsync(ct);
}
