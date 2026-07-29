using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminStations;

public sealed class GetAdminStationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<PagedResult<Station>> HandleAsync(GetAdminStationsQuery query, CancellationToken ct = default)
    {
        var paged = new PagedRequest(query.Page, query.PageSize);
        return await _context.Stations
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToPagedResultAsync(paged, ct);
    }
}
