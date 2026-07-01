using FuelFlow.Persistence;
using FuelFlow.Features.Orders.SharedModels;
using Microsoft.EntityFrameworkCore;
using FuelFlow.API.Features.Monobank.ProcessWebhook;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed class ProcessMonobankWebhookCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessMonobankWebhookCommandHandler> _logger;

    public ProcessMonobankWebhookCommandHandler(
        ApplicationDbContext context,
        ILogger<ProcessMonobankWebhookCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ProcessMonobankWebhookResponse> HandleAsync(
        ProcessMonobankWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing Monobank webhook for invoice {InvoiceId}, status: {Status}",
            command.InvoiceId,
            command.Status);

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.MonobankInvoiceId == command.InvoiceId, cancellationToken);

        if (order == null)
        {
            _logger.LogWarning("Order not found for Monobank invoice {InvoiceId}", command.InvoiceId);
            return new ProcessMonobankWebhookResponse
            {
                Success = false,
                Message = $"Order not found for invoice {command.InvoiceId}"
            };
        }

        _logger.LogInformation("Found order {OrderId} for invoice {InvoiceId}, current status: {CurrentStatus}", order.Id, command.InvoiceId, order.Status);

        var previousStatus = order.Status;

        switch (command.Status.ToLowerInvariant())
        {
            case "success":
                order.Status = OrderStatus.Paid;
                _logger.LogInformation("Order {OrderId} marked as Paid", order.Id);
                break;

            case "failure":
            case "reversed":
                order.Status = OrderStatus.Cancelled;
                _logger.LogInformation("Order {OrderId} marked as Cancelled due to payment {Status}", order.Id, command.Status);
                break;

            case "processing":
            case "hold":
                _logger.LogInformation("Order {OrderId} still processing, status: {Status}", order.Id, command.Status);
                break;

            case "created":
                _logger.LogInformation("Order {OrderId} invoice created, awaiting payment", order.Id);
                break;

            default:
                _logger.LogWarning("Unknown Monobank status {Status} for order {OrderId}", command.Status, order.Id);
                break;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} status updated from {PreviousStatus} to {NewStatus}",
            order.Id,
            previousStatus,
            order.Status);

        return new ProcessMonobankWebhookResponse
        {
            Success = true,
            OrderId = order.Id.ToString(),
            PreviousStatus = previousStatus.ToString(),
            NewStatus = order.Status.ToString(),
            Message = $"Order {order.Id} updated to {order.Status}"
        };
    }
}