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
// The fuel's sizes joined to the offers that sell them: the brand card reads a per-litre figure per
    // package, and that figure belongs to the offer. Projected in one query so the card is not
    // assembled N times.
    var packages = await _context.FuelOffers
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
