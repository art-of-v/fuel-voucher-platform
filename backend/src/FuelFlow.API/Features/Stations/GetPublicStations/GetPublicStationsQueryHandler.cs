using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetPublicStations;

public sealed class GetPublicStationsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetPublicStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<Station>> HandleAsync(GetPublicStationsQuery query, CancellationToken ct = default) =>
        await _context.Stations.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
}
