using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Purchases.GetAdminPurchases;

public sealed class GetAdminPurchasesQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPurchasesQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<Purchase>> HandleAsync(GetAdminPurchasesQuery query, CancellationToken ct = default) =>
        await _context.Purchases.AsNoTracking().Include(p => p.User).OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);
}
