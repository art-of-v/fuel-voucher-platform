using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStationNodes;

public sealed class GetPublicStationNodesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationNodesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<StationNode>> HandleAsync(GetPublicStationNodesQuery query, CancellationToken ct = default) =>
        await _context.StationNodes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
}
