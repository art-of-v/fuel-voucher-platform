using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetProviderById;

public sealed class GetBrandByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetBrandByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BrandDto?> HandleAsync(GetBrandByIdQuery query, CancellationToken ct = default)
    {
        var Brand = await _context.Brands.AsNoTracking().FirstOrDefaultAsync(s => s.Id == query.Id, ct);
        if (Brand is null) return null;

        var fuels = await _context.FuelTypes.AsNoTracking().Where(f => f.BrandId == query.Id).ToListAsync(ct);
        var fuelIds = fuels.Select(f => f.Id).ToList();
// Same single projected query as GetBrandsQueryHandler, narrowed to this brand's fuels.
    var packages = await _context.FuelOffers
    .AsNoTracking()
    .Project(_context.FuelNominals.AsNoTracking())
    .Where(p => fuelIds.Contains(p.FuelTypeId))
    .ToListAsync(ct);

        var fuelDtos = fuels
            .Select(f => BrandFuelCard.Build(f, packages.Where(p => p.FuelTypeId == f.Id).ToList()))
            .OrderBy(f => f.Name).ToList();

        return new BrandDto
        {
            Id = Brand.Id,
            Name = Brand.Name,
            LogoText = Brand.LogoText,
            Color = Brand.Color,
            Fuels = fuelDtos,
            Nominals = fuelDtos.SelectMany(f => f.PackageLiters).Distinct().OrderBy(l => l).ToList()
        };
    }
}
