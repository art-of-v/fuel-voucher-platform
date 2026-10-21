using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
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
        // The default is TrackAll (DatabaseSetup), so this already tracks. Kept explicit because this
        // is a soft delete — we mutate IsDeleted below — and SaveChanges has to see it or the
        // order reappears on the next refresh.
        var order = await _context.Orders
            .AsTracking()
            .FirstOrDefaultAsync(
                o => o.Id == command.OrderId
                     && o.UserId == command.UserId
                     && o.Status == OrderStatus.PendingPayment,
                cancellationToken);
        if (order is null) return false;

        order.IsDeleted = true;
        order.UpdatedAtUtc = SystemClock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
