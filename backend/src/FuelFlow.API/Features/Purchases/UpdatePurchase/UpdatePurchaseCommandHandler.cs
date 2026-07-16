using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Purchases.UpdatePurchase;

public sealed class UpdatePurchaseCommandHandler
{
    private readonly ApplicationDbContext _context;
    public UpdatePurchaseCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdatePurchaseCommand command, CancellationToken ct = default)
    {
        var entity = await _context.Purchases.FirstOrDefaultAsync(p => p.Id == command.Id, ct);
        if (entity is null) return false;

        if (!string.IsNullOrWhiteSpace(command.Status))
            entity.Status = command.Status;

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
