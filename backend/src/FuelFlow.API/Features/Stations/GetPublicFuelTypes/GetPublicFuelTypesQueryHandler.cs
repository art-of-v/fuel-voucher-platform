using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicFuelTypes;

public sealed class GetPublicFuelTypesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicFuelTypesQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>
    /// Materialised and mapped rather than projected, because the slug is needed and
    /// <c>fuel_types</c> has no navigation to <c>brands</c>: <c>fuel_types.brand_id</c> carries no
    /// foreign key, so EF cannot join it. Adding one is a schema change beyond this slice, and the
    /// catalogue is a few hundred rows, so one dictionary beats one migration.
    /// </summary>
    public async Task<List<PublicFuelTypeResponse>> HandleAsync(GetPublicFuelTypesQuery query, CancellationToken ct = default)
    {
        var fuels = await _context.FuelTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.BrandId, x.BasePrice, x.DiscountPrice })
            .ToListAsync(ct);

        var slugs = await _context.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Slug, ct);

        return fuels
            .Select(f => new PublicFuelTypeResponse(
                f.Id,
                f.BrandId,
                slugs.TryGetValue(f.BrandId, out var slug) ? slug : string.Empty,
                f.Name,
                f.BasePrice,
                f.DiscountPrice))
            .ToList();
    }
}
