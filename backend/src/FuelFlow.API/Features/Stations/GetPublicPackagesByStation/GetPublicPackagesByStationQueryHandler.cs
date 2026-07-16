using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicPackagesByStation;

public sealed class GetPublicPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicPackagesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetPublicPackagesByStationQuery query, CancellationToken ct = default) =>
        await _context.FuelPackages
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
            .OrderBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .ToListAsync(ct);
}
