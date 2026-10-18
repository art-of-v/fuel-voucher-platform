using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetAdminPackagesByBrand;

public sealed class GetAdminPackagesByBrandQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPackagesByBrandQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetAdminPackagesByBrandQuery query, CancellationToken ct = default)
    {
        // Same join and same PackageProjection as GetAdminPackagesQueryHandler, narrowed to one Brand.
        var fuels = await _context.FuelTypes.AsNoTracking()
            .ToDictionaryAsync(f => f.Id, ct);

        // One brand, so this is one row - but the projection still needs a dictionary, and reading it
        // the same way as the unfiltered handler is what keeps the two from drifting.
        var brandSlugs = await _context.Brands.AsNoTracking()
            .Where(b => b.Id == query.BrandId)
            .ToDictionaryAsync(b => b.Id, b => b.Slug, ct);

        var volumes = await _context.FuelOfferVolumes
            .AsNoTracking()
            // Same reason as GetAdminPackagesQueryHandler: the projection needs the offer loaded.
            .Include(v => v.Offer)
            .Where(v => v.Offer.BrandId == query.BrandId)
            .OrderBy(v => v.Offer.FuelTypeId)
            .ThenBy(v => v.Liters)
            .ToListAsync(ct);

        return volumes.ToPackages(fuels, brandSlugs);
    }
}
