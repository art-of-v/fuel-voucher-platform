using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.DeleteMyOrder;

public sealed class DeleteMyOrderCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteMyOrderCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HandleAsync(
        DeleteMyOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        // Ownership is part of the key: a non-owned or already-deleted order is a no-op
        // (the entity's query filter hides soft-deleted rows). Only an unpaid checkout may
        // go this way — anything past PendingPayment has money or vouchers attached and
        // belongs to the admin/refund flows.
        // Global query default is NoTracking (DatabaseSetup). This is a soft delete — we
        // mutate IsDeleted below — so the entity must be tracked or SaveChanges silently
        // persists nothing and the order reappears on the next refresh.
        var order = await _context.Orders
            .AsTracking()
            .FirstOrDefaultAsync(
                o => o.Id == command.OrderId
                     && o.UserId == command.UserId
                     && o.Status == OrderStatus.PendingPayment,
                cancellationToken);
        if (order is null) return false;

        order.IsDeleted = true;
        order.UpdatedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
