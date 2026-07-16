using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.GetAdminFuelTypeById;

public sealed class GetAdminFuelTypeByIdQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminFuelTypeByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<FuelTypeEntity?> HandleAsync(GetAdminFuelTypeByIdQuery query, CancellationToken ct = default) =>
        await _context.FuelTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
}
