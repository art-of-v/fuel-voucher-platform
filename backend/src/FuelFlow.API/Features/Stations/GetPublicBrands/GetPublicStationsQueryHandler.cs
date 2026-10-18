using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicBrands;

public sealed class GetPublicBrandsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicBrandsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicBrandResponse>> HandleAsync(GetPublicBrandsQuery query, CancellationToken ct = default) =>
        await _context.Brands
            .AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new PublicBrandResponse(
                x.Id,
                x.Name,
                x.Color,
                x.LogoText,
                x.SortOrder))
            .ToListAsync(ct);
}
