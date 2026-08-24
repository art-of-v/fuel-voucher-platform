using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SimulatePayment;

/// <summary>
/// Raised when a simulated payment would move an order through a transition the real
/// Monobank webhook would refuse. Carries no state detail: the caller supplies the order id,
/// so there is nothing to tell them they do not already know.
/// </summary>
public sealed class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException()
        : base("Order is not in a state that can accept this payment result")
    {
    }
}

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
            // Same guard the real webhook uses. Without it this endpoint could cancel an
            // already-Fulfilled or Refunded order, which no payment provider can do.
            if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.Cancelled))
            {
                _logger.LogWarning(
                    "Payment simulation rejected for order {OrderId}: cannot move {Status} -> Cancelled",
                    command.OrderId,
                    order.Status);

                throw new InvalidOrderStateException();
            }

            order.Status = OrderStatus.Cancelled;
            order.MonobankStatus = MonobankStatus.Failure;
            order.UpdatedAtUtc = DateTime.UtcNow;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment simulation failed for order {OrderId}", command.OrderId);

            return new SimulatePaymentResponse
            {
                Status = "failed"
            };
        }

        if (order.Status != OrderStatus.PendingFulfillment)
        {
            // The old code force-assigned PendingFulfillment from *any* state, so an admin
            // could resurrect a Fulfilled order and have fulfilment hand out a second set of
            // vouchers for a single payment. Terminal states must stay terminal here too.
            if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.PendingFulfillment))
            {
                _logger.LogWarning(
                    "Payment simulation rejected for order {OrderId}: cannot move {Status} -> PendingFulfillment",
                    command.OrderId,
                    order.Status);

                throw new InvalidOrderStateException();
            }

            order.Status = OrderStatus.PendingFulfillment;
            order.MonobankStatus = MonobankStatus.Success;
            order.UpdatedAtUtc = DateTime.UtcNow;
            _context.Orders.Update(order);

            // Match the orderId field with jsonb containment, NOT a substring LIKE. payload is
            // a jsonb column and Postgres has no `jsonb ~~ jsonb` (LIKE) operator, so
            // String.Contains threw 42883 here (same defect as the real webhook path).
            var orderProbe = System.Text.Json.JsonSerializer.Serialize(new { orderId = order.Id });
            var existingEvent = await _context.OutboxEvents
                .Where(e => e.EventType == OutboxEventType.OrderCreated
                         && EF.Functions.JsonContains(e.Payload, orderProbe))
                .FirstOrDefaultAsync(cancellationToken);

            if (existingEvent == null)
            {
                var firstLi = order.LineItems.FirstOrDefault();
                var outboxEvent = new OutboxEvent
                {
                    EventType = OutboxEventType.OrderCreated,
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        orderId = order.Id,
                        userId = order.UserId,
                        provider = firstLi?.Provider ?? "",
                        fuelType = firstLi?.FuelTypeId ?? "",
                        liters = firstLi?.Liters ?? 0m,
                        quantity = firstLi?.Quantity ?? 0
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
