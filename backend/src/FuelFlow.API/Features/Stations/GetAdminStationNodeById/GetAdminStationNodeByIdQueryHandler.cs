using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminStationNodeById;

public sealed class GetAdminStationNodeByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminStationNodeByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<StationNode?> HandleAsync(GetAdminStationNodeByIdQuery query, CancellationToken ct = default) =>
        await _context.StationNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == query.Id, ct);
}
