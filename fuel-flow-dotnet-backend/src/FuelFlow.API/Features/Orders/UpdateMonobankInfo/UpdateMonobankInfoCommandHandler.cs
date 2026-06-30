using FuelFlow.Persistence;

namespace FuelFlow.Features.Orders.UpdateMonobankInfo;

public sealed class UpdateMonobankInfoCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<UpdateMonobankInfoCommandHandler> _logger;

    public UpdateMonobankInfoCommandHandler(
        ApplicationDbContext context,
        ILogger<UpdateMonobankInfoCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task HandleAsync(
        UpdateMonobankInfoCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { command.OrderId }, cancellationToken);

        if (order == null)
        {
            throw new InvalidOperationException($"Order {command.OrderId} not found");
        }

        order.MonobankInvoiceId = command.InvoiceId;
        order.MonobankStatus = command.Status;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated Monobank info for order {OrderId}: {InvoiceId} - {Status}",
            command.OrderId, command.InvoiceId, command.Status);
    }
}
