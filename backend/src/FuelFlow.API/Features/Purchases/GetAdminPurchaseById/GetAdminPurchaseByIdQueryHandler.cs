using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Purchases.GetAdminPurchaseById;

public sealed class GetAdminPurchaseByIdQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminPurchaseByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<Purchase?> HandleAsync(GetAdminPurchaseByIdQuery query, CancellationToken ct = default) =>
        await _context.Purchases.AsNoTracking().Include(p => p.User).FirstOrDefaultAsync(p => p.Id == query.Id, ct);
}
