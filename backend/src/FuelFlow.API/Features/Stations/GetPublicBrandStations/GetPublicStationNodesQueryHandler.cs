using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicBrandStations;

public sealed class GetPublicBrandStationsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicBrandStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicBrandStationResponse>> HandleAsync(GetPublicBrandStationsQuery query, CancellationToken ct = default) =>
        await _context.BrandStations
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new PublicBrandStationResponse(
                x.Id, x.BrandId, x.Name, x.Address, x.Phone, x.City, x.StationType, x.Lat, x.Lng))
            .ToListAsync(ct);
}
