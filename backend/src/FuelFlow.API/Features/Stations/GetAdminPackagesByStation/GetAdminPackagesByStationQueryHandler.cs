using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminPackagesByStation;

public sealed class GetAdminPackagesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPackagesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetAdminPackagesByStationQuery query, CancellationToken ct = default)
    {
        // Same join and same PackageProjection as GetAdminPackagesQueryHandler, narrowed to one station.
        var fuelNames = await _context.FuelTypes.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var volumes = await _context.FuelOfferVolumes
            .AsNoTracking()
            .Where(v => v.Offer.StationId == query.StationId)
            .OrderBy(v => v.Offer.FuelTypeId)
            .ThenBy(v => v.Liters)
            .ToListAsync(ct);

        return volumes.ToPackages(fuelNames);
    }
}
