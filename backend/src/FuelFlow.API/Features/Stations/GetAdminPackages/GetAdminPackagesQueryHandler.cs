using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminPackages;

public sealed class GetAdminPackagesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPackagesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelPackage>> HandleAsync(GetAdminPackagesQuery query, CancellationToken ct = default) =>
        await _context.FuelPackages
            .AsNoTracking()
            .OrderBy(x => x.StationId)
            .ThenBy(x => x.FuelName)
            .ThenBy(x => x.Liters)
            .ToListAsync(ct);
}
