using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminStationNodes;

public sealed class GetAdminStationNodesQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminStationNodesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<PagedResult<StationNode>> HandleAsync(GetAdminStationNodesQuery query, CancellationToken ct = default)
    {
        var paged = new PagedRequest(query.Page, query.PageSize);
        var source = _context.StationNodes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.StationId))
            source = source.Where(n => n.StationId == query.StationId);

        return await source
            .OrderBy(n => n.StationId)
            .ThenBy(n => n.Name)
            .ToPagedResultAsync(paged, ct);
    }
}
