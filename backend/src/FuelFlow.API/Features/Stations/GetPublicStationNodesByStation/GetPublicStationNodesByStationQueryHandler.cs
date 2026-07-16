using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStationNodesByStation;

public sealed class GetPublicStationNodesByStationQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationNodesByStationQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<StationNode>> HandleAsync(GetPublicStationNodesByStationQuery query, CancellationToken ct = default) =>
        await _context.StationNodes
            .AsNoTracking()
            .Where(x => x.StationId == query.StationId)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
}
