using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminPackagesByStation;

public sealed class GetAdminPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPackagesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetAdminPackagesByStationQuery query, CancellationToken ct = default) =>
        await _context.FuelPackages
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
            .OrderBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .ToListAsync(ct);
}
