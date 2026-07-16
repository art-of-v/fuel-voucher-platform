using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.UpdateOrderStatus;

public sealed class UpdateOrderStatusCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HandleAsync(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (entity is null) return false;

        if (!string.IsNullOrWhiteSpace(command.Status) && Enum.TryParse<OrderStatus>(command.Status, out var parsedStatus))
            entity.Status = parsedStatus;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
