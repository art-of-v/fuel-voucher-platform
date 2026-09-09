using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStations;

public sealed class GetPublicStationsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicStationResponse>> HandleAsync(GetPublicStationsQuery query, CancellationToken ct = default) =>
        await _context.Stations
            .AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new PublicStationResponse(
                x.Id,
                x.Name,
                x.Color,
                x.LogoText,
                x.Address,
                x.Phone,
                x.StationType,
                x.Lat,
                x.Lng,
                x.SortOrder))
            .ToListAsync(ct);
}
