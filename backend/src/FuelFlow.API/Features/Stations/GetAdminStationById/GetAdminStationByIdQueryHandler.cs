using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminStationById;

public sealed class GetAdminStationByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminStationByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<Station?> HandleAsync(GetAdminStationByIdQuery query, CancellationToken ct = default) =>
        await _context.Stations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
}
