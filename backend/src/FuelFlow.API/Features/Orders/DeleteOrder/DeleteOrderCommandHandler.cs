using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.DeleteOrder;

public sealed class DeleteOrderCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteOrderCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HandleAsync(
        DeleteOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (entity is null) return false;

        // fuel_vouchers.order_id is Restrict, and deliberately so: an order that put fuel into
        // someone's hands is the only record of where that fuel came from. Deleting it would
        // either be refused by the database with an opaque error or - before the FK existed -
        // silently orphan every voucher it ever delivered. Say so instead.
        var deliveredCount = await _context.FuelVouchers
            .AsNoTracking()
            .CountAsync(v => v.OrderId == command.Id, cancellationToken);

        if (deliveredCount > 0)
        {
            throw new ArgumentException(
                $"This order delivered {deliveredCount} voucher(s) and cannot be deleted while they are still recorded against it.");
        }

        _context.Orders.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
