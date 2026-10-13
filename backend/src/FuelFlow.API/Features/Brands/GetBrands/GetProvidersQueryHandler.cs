using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetProviders;

public sealed class GetBrandsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetBrandsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<BrandDto>> HandleAsync(GetBrandsQuery query, CancellationToken ct = default)
    {
        var stations = await _context.Stations.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);
        var fuels = await _context.FuelTypes.AsNoTracking().ToListAsync(ct);
        var packages = await _context.FuelPackages.AsNoTracking().ToListAsync(ct);

        return stations.Select(station =>
        {
            var stationFuels = fuels.Where(f => f.StationId == station.Id).ToList();
            var fuelDtos = stationFuels
                .Select(f => BrandFuelCard.Build(f, packages.Where(p => p.FuelTypeId == f.Id).ToList()))
                .OrderBy(f => f.Name).ToList();

            return new BrandDto
            {
                Id = station.Id,
                Name = station.Name,
                LogoText = station.LogoText,
                Color = station.Color,
                SortOrder = station.SortOrder,
                Fuels = fuelDtos,
                Nominals = fuelDtos.SelectMany(f => f.PackageLiters).Distinct().OrderBy(l => l).ToList()
            };
        }).ToList();
    }
}
