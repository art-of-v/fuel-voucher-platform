using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SimulatePayment;

public sealed class SimulatePaymentCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly GetUserPurchasesCommandHandler _getUserPurchasesHandler;
    private readonly ILogger<SimulatePaymentCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public SimulatePaymentCommandHandler(
        ApplicationDbContext context,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        ILogger<SimulatePaymentCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<SimulatePaymentResponse> HandleAsync(
        SimulatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { command.OrderId }, cancellationToken);

        if (order == null)
        {
            throw new InvalidOperationException($"Order {command.OrderId} not found");
        }

        if (command.Scenario == MonobankStatus.Failure.ToString().ToLower())
        {
            order.Status = OrderStatus.Cancelled;
            order.MonobankStatus = MonobankStatus.Failure;
            order.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment simulation failed for order {OrderId}", command.OrderId);

            return new SimulatePaymentResponse
            {
                Status = "failed"
            };
        }

        if (order.Status != OrderStatus.PendingFulfillment)
        {
            order.Status = OrderStatus.PendingFulfillment;
            order.MonobankStatus = MonobankStatus.Success;
            order.UpdatedAtUtc = DateTime.UtcNow;

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
            }

            await _context.SaveChangesAsync(cancellationToken);

            _backgroundJobClient.Enqueue<FulfillmentService>(
                s => s.ProcessPendingOrdersAsync(CancellationToken.None));
        }

        _logger.LogInformation("Payment simulation succeeded for order {OrderId}", command.OrderId);

        var purchases = await _getUserPurchasesHandler.HandleAsync(
            new GetUserPurchasesCommand(order.UserId),
            cancellationToken);

        var purchase = purchases.FirstOrDefault(p => p.Id == command.OrderId);

        return new SimulatePaymentResponse
        {
            Status = "success",
            Purchase = purchase
        };
    }
}
