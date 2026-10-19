using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetProviders;

public sealed class GetBrandsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetBrandsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<BrandDto>> HandleAsync(GetBrandsQuery query, CancellationToken ct = default)
    {
        var Brands = await _context.Brands.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(ct);
        var fuels = await _context.FuelTypes.AsNoTracking().ToListAsync(ct);
// The fuels and their sizes. The card reads a per-litre figure per package, and every one of
        // those figures is the fuel's own, so the projection takes fuels and nothing else. Projected
        // in one query so the card is not assembled N times.
        var packages = await _context.FuelTypes
            .AsNoTracking()
            .Project(_context.FuelNominals.AsNoTracking(), _context.Brands.AsNoTracking())
            .ToListAsync(ct);

        return Brands.Select(Brand =>
        {
            var BrandFuels = fuels.Where(f => f.BrandId == Brand.Id).ToList();
            var fuelDtos = BrandFuels
                .Select(f => BrandFuelCard.Build(f, packages.Where(p => p.FuelTypeId == f.Id).ToList()))
                .OrderBy(f => f.Name).ToList();

            return new BrandDto
            {
                Id = Brand.Id,
                Slug = Brand.Slug,
                Name = Brand.Name,
                LogoText = Brand.LogoText,
                Color = Brand.Color,
                SortOrder = Brand.SortOrder,
                Fuels = fuelDtos,
                Nominals = fuelDtos.SelectMany(f => f.PackageLiters).Distinct().OrderBy(l => l).ToList()
            };
        }).ToList();
    }
}
