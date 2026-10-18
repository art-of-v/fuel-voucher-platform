using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetAdminBrandById;

public sealed class GetAdminBrandByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminBrandByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<Brand?> HandleAsync(GetAdminBrandByIdQuery query, CancellationToken ct = default) =>
        await _context.Brands.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.Id, ct);
}
