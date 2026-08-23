using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStationNodes;

public sealed class GetPublicStationNodesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationNodesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<PublicStationNodeResponse>> HandleAsync(GetPublicStationNodesQuery query, CancellationToken ct = default) =>
        await _context.StationNodes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new PublicStationNodeResponse(
                x.Id, x.StationId, x.Name, x.Address, x.Phone, x.City, x.StationType, x.Lat, x.Lng))
            .ToListAsync(ct);
}
