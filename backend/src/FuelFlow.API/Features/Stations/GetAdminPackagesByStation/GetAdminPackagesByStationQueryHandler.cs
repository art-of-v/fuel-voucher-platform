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
        var fuels = await _context.FuelTypes.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, ct);

        var volumes = await _context.FuelOfferVolumes
            .AsNoTracking()
            // Same reason as GetAdminPackagesQueryHandler: the projection needs the offer loaded.
            .Include(v => v.Offer)
            .Where(v => v.Offer.StationId == query.StationId)
            .OrderBy(v => v.Offer.FuelTypeId)
            .ThenBy(v => v.Liters)
            .ToListAsync(ct);

        return volumes.ToPackages(fuels);
    }
}
