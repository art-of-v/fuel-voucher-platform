using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Purchases.DeletePurchase;

public sealed class DeletePurchaseCommandHandler
{
    private readonly ApplicationDbContext _context;
    public DeletePurchaseCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeletePurchaseCommand command, CancellationToken ct = default)
    {
        var entity = await _context.Purchases.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (entity is null) return false;

        _context.Purchases.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
