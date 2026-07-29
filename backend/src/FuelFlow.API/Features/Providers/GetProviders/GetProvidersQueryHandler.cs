using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers.GetProviders;

public sealed class GetProvidersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetProvidersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<ProviderDto>> HandleAsync(GetProvidersQuery query, CancellationToken ct = default)
    {
        var stations = await _context.Stations.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var fuels = await _context.FuelTypes.AsNoTracking().ToListAsync(ct);
        var packages = await _context.FuelPackages.AsNoTracking().ToListAsync(ct);

        return stations.Select(station =>
        {
            var stationFuels = fuels.Where(f => f.StationId == station.Id).ToList();
            var fuelDtos = stationFuels.Select(f =>
            {
                var fuelPackages = packages.Where(p => p.FuelTypeId == f.Id).ToList();
                var firstPkg = fuelPackages.FirstOrDefault();
                return new ProviderFuelDto
                {
                    Id = f.Id,
                    Name = f.Name,
                    SupplierPricePerLiter = firstPkg?.SupplierPricePerLiter ?? 0,
                    MarginUahPerLiter = firstPkg?.MarginUahPerLiter ?? 0,
                    MarginPercent = firstPkg?.MarginPercent,
                    FinalPricePerLiter = firstPkg?.FinalPricePerLiter ?? 0,
                    PackageLiters = fuelPackages.Select(p => (int)p.Liters).OrderBy(l => l).ToList()
                };
            }).OrderBy(f => f.Name).ToList();

            return new ProviderDto
            {
                Id = station.Id,
                Name = station.Name,
                LogoText = station.LogoText,
                Color = station.Color,
                Fuels = fuelDtos,
                Nominals = fuelDtos.SelectMany(f => f.PackageLiters).Distinct().OrderBy(l => l).ToList()
            };
        }).ToList();
    }
}
