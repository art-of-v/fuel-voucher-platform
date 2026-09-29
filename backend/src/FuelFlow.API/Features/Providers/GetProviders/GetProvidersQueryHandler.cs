using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers.GetProviders;

public sealed class GetProvidersQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetProvidersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<ProviderDto>> HandleAsync(GetProvidersQuery query, CancellationToken ct = default)
    {
        var stations = await _context.Stations.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);
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
                    PumpPricePerLiter = firstPkg?.PumpPricePerLiter,
                    // Fall back to the historical marketing discount (base - final) so a fuel
                    // priced before the min-discount column existed shows a sane initial knob.
                    MinDiscountPerLiter = firstPkg?.MinDiscountPerLiter ?? Math.Max(0, f.BasePrice - f.DiscountPrice),
                    // base_price is the pump/reference price; the actual customer discount
                    // is base - final. Derived here (display only) so the admin UI can show it.
                    DiscountPerLiter = Math.Max(0, f.BasePrice - f.DiscountPrice),
                    AllowBelowCost = f.AllowBelowCost,
                    PackageLiters = fuelPackages.Select(p => (int)p.Liters).OrderBy(l => l).ToList()
                };
            }).OrderBy(f => f.Name).ToList();

            return new ProviderDto
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
