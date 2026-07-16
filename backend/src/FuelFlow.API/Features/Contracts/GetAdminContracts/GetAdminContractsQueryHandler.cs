using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Contracts.GetAdminContracts;

public sealed class GetAdminContractsQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminContractsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<Contract>> HandleAsync(GetAdminContractsQuery query, CancellationToken ct = default) =>
        await _context.Contracts.AsNoTracking()
            .Include(c => c.User).Include(c => c.Entity).Include(c => c.Station)
            .OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct);
}
