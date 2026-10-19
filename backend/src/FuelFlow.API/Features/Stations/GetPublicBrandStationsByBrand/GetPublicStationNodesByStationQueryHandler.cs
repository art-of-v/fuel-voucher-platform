using FuelFlow.Features.Brands.GetPublicBrandStations;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicBrandStationsByBrand;

public sealed class GetPublicBrandStationsByBrandQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicBrandStationsByBrandQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicBrandStationResponse>> HandleAsync(GetPublicBrandStationsByBrandQuery query, CancellationToken ct = default) =>
        await _context.BrandStations
            .AsNoTracking()
            .Where(x => x.BrandId == query.BrandId)
            .OrderBy(x => EF.Property<string>(x, "NameSortKey"))
            .ThenBy(x => x.Name)
            .Select(x => new PublicBrandStationResponse(
                x.Id, x.BrandId, x.Brand!.Slug, x.Name, x.Address, x.Phone, x.City, x.StationType, x.Lat, x.Lng))
            .ToListAsync(ct);
}
