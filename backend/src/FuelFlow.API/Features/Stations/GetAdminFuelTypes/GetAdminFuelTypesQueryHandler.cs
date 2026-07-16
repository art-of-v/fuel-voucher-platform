using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminFuelTypes;

public sealed class GetAdminFuelTypesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminFuelTypesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<FuelTypeEntity>> HandleAsync(GetAdminFuelTypesQuery query, CancellationToken ct = default) =>
        await _context.FuelTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
}
