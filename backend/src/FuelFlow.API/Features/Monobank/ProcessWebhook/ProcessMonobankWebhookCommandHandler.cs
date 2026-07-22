using FuelFlow.API.BackgroundJobs;
using FuelFlow.Persistence;
using FuelFlow.Features.Orders.SharedModels;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using FuelFlow.API.Features.Monobank.ProcessWebhook;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed class ProcessMonobankWebhookCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProcessMonobankWebhookCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public ProcessMonobankWebhookCommandHandler(
        ApplicationDbContext context,
        ILogger<ProcessMonobankWebhookCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
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
            .Include(o => o.LineItems)
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
                order.Status = OrderStatus.PendingFulfillment;
                order.MonobankStatus = MonobankStatus.Success;
                order.UpdatedAtUtc = DateTime.UtcNow;
                _logger.LogInformation("Order {OrderId} marked as PendingFulfillment", order.Id);

                var existingEvents = await _context.OutboxEvents
                    .Where(e => e.EventType == OutboxEventType.OrderCreated)
                    .ToListAsync(cancellationToken);

                var existingEvent = existingEvents
                    .FirstOrDefault(e => e.Payload.Contains(order.Id.ToString(), StringComparison.Ordinal));

                if (existingEvent == null)
                {
                    var outboxEvent = new OutboxEvent
                    {
                        EventType = OutboxEventType.OrderCreated,
                        Payload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            orderId = order.Id,
                            userId = order.UserId,
                            provider = order.Provider,
                            fuelType = order.FuelTypeId,
                            liters = order.Liters,
                            quantity = order.Quantity
                        }),
                        Processed = false,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _context.OutboxEvents.Add(outboxEvent);
                    _logger.LogInformation("Created ORDER_CREATED outbox event for order {OrderId}", order.Id);
                }
                break;

            case "failure":
            case "reversed":
                order.Status = OrderStatus.Cancelled;
                order.UpdatedAtUtc = DateTime.UtcNow;
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

        if (order.Status == OrderStatus.PendingFulfillment)
        {
            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

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
