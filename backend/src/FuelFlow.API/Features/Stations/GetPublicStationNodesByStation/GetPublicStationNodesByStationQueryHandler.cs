using FuelFlow.Features.Stations.GetPublicStationNodes;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStationNodesByStation;

public sealed class GetPublicStationNodesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationNodesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicStationNodeResponse>> HandleAsync(GetPublicStationNodesByStationQuery query, CancellationToken ct = default) =>
        await _context.StationNodes
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
            .OrderBy(x => x.Name)
            .Select(x => new PublicStationNodeResponse(
                x.Id, x.StationId, x.Name, x.Address, x.Phone, x.City, x.StationType, x.Lat, x.Lng))
            .ToListAsync(ct);
}
