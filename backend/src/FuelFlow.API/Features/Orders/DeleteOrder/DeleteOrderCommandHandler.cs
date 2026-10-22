using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.DeleteOrder;

/// deleted order is still a row, and the handler is also how an operator retires an order they
    /// no longer want to see. Flipping the flag keeps that reversible.
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
        // AsTracking is required, not decoration: the context default is TrackAll, so setting
        // IsDeleted on an untracked entity persists NOTHING and this handler would report success
        // while the order stayed exactly where it was. The architecture guard
        // (NoTrackingMutationGuardTests) enforces it - it caught the first attempt at this change.
        var entity = await _context.Orders
            .AsTracking()
            .FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (entity is null) return false;

        // fuel_vouchers.order_id is Restrict, and deliberately so: an order that put fuel into
        // someone's hands is the only record of where that fuel came from. Deleting it would
        // either be refused by the database with an opaque error or - before the FK existed -
        // silently orphan every voucher it ever delivered. Say so instead.
        //
        // IncludingSoftDeleted because a soft-deleted voucher is still a row the FK counts; letting
        // the !IsDeleted filter hide it here would pass this guard and then fail on the constraint.
        var deliveredCount = await _context.FuelVouchers
            .IncludingSoftDeleted()
            .AsNoTracking()
            .CountAsync(v => v.OrderId == command.Id, cancellationToken);

        if (deliveredCount > 0)
        {
            throw new ArgumentException(
                $"This order delivered {deliveredCount} voucher(s) and cannot be deleted while they are still recorded against it.");
        }

        // Soft delete, never Remove. An order is history: it is what an auditor reads to answer
        // "where did this fuel come from", and the owner's policy (#217) keeps anything that
        // affects history forever. Remove also had a second effect nobody asked for -
        // fulfillments cascade from their order, so hard-deleting quietly took the delivery
        // records with it.
        entity.IsDeleted = true;
        entity.UpdatedAtUtc = SystemClock.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
