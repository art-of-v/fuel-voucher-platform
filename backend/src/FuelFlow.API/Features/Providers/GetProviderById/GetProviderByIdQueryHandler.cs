using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers.GetProviderById;

public sealed class GetProviderByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetProviderByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ProviderDto?> HandleAsync(GetProviderByIdQuery query, CancellationToken ct = default)
    {
        var station = await _context.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.Id, ct);
        if (station is null) return null;

        var fuels = await _context.FuelTypes.AsNoTracking().Where(f => f.StationId == query.Id).ToListAsync(ct);
        var fuelIds = fuels.Select(f => f.Id).ToList();
        var packages = await _context.FuelPackages.AsNoTracking()
            .Where(p => fuelIds.Contains(p.FuelTypeId)).ToListAsync(ct);

        var fuelDtos = fuels
            .Select(f => ProviderFuelCard.Build(f, packages.Where(p => p.FuelTypeId == f.Id).ToList()))
            .OrderBy(f => f.Name).ToList();

        return new ProviderDto
        {
            Id = station.Id,
            Name = station.Name,
            LogoText = station.LogoText,
            Color = station.Color,
            Fuels = fuelDtos,
            Nominals = fuelDtos.SelectMany(f => f.PackageLiters).Distinct().OrderBy(l => l).ToList()
        };
    }
}
