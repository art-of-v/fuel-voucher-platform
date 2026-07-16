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

        _context.Orders.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
