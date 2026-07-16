using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminStations;

public sealed class GetAdminStationsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminStationsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<Station>> HandleAsync(GetAdminStationsQuery query, CancellationToken ct = default) =>
        await _context.Stations.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
}
