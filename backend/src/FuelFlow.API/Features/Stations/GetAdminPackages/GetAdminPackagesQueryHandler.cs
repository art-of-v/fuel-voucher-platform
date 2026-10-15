using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminPackages;

public sealed class GetAdminPackagesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPackagesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetAdminPackagesQuery query, CancellationToken ct = default)
    {
        // The admin list still speaks the package shape, so volumes are joined to their offers and
        // rebuilt through PackageProjection. Fuel names come from fuel_types in the same query rather
        // than from a per-package string: the name used to be copied onto every package row, and two
        // rows for the same fuel could disagree about what the fuel is called.
        var fuelNames = await _context.FuelTypes.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        var volumes = await _context.FuelOfferVolumes
            .AsNoTracking()
            .OrderBy(v => v.Offer.StationId)
            .ThenBy(v => v.Offer.FuelTypeId)
            .ThenBy(v => v.Liters)
            .ToListAsync(ct);

        return volumes.ToPackages(fuelNames);
    }
}
