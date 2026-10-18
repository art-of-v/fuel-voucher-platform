using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetAdminBrandStationById;

public sealed class GetAdminBrandStationByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBrandStationByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<BrandStation?> HandleAsync(GetAdminBrandStationByIdQuery query, CancellationToken ct = default) =>
        await _context.BrandStations.AsNoTracking().FirstOrDefaultAsync(n => n.Id == query.Id, ct);
}
