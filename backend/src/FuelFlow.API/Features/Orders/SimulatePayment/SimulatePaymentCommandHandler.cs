using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
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
            // already-Fulfilled or Refunded order, which no payment brand can do.
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

            // The money row follows the order, exactly as the real webhook does it: the acquirer
            // says the money moved, and the payment record says so too. It used to be left at
            // Pending, which left the simulation claiming a payment that the ledger said had not
            // happened - and, now that fulfilment asks the money row whether the order was paid
            // for, it would have refused the order the simulation had just paid for.
            await MarkCaptureAsSucceededAsync(order, cancellationToken);

            // A renewal order fulfils on its own path (extend/replace existing vouchers). It must not
            // write an ORDER_CREATED event (that drives the buy-fuel handler, which mints fresh stock),
            // and it is handed to ProcessRenewalOrderAsync — mirrors the real webhook handler so a
            // simulated renewal payment behaves like a real one in Development.
            var isRenewalOrder = order.Kind == OrderKind.Renewal;

            if (!isRenewalOrder)
            {
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
                            brand = firstLi?.Brand ?? "",
                            fuelType = firstLi?.FuelTypeId ?? Guid.Empty,
                            liters = firstLi?.Liters ?? 0m,
                            quantity = firstLi?.Quantity ?? 0
                        }),
                        Processed = false,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    _context.OutboxEvents.Add(outboxEvent);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (isRenewalOrder)
            {
                _backgroundJobClient.Enqueue<FulfillmentService>(
                    s => s.ProcessRenewalOrderAsync(order.Id, CancellationToken.None));
            }
            else
            {
                _backgroundJobClient.Enqueue<FulfillmentService>(
                    s => s.ProcessPendingOrdersAsync(CancellationToken.None));
            }
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

    /// <summary>
    /// Marks the order's capture payment as succeeded - the state a real payment leaves behind.
    /// Creates the row when checkout never wrote one (the invoice creation is the thing that
    /// writes it, and a simulated payment can arrive for an order whose invoice failed).
    /// </summary>
    private async Task MarkCaptureAsSucceededAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == order.Id && p.Kind == PaymentKind.Capture, cancellationToken);

        var isNew = payment is null;
        if (payment is null)
        {
            // A zero-price order has no money to record, and the database refuses a zero-amount
            // capture - so there is nothing to write, and the order stays unpaid (which is the
            // safe direction: no money recorded, no fuel handed over).
            if (order.Price <= 0)
                return;

            payment = PaymentFactory.CreateCapture(
                order,
                order.MonobankMerchant ?? MonobankMerchant.Sandbox,
                order.MonobankInvoiceId ?? $"simulated-{order.Id:N}",
                DateTime.UtcNow);
            _context.Payments.Add(payment);
        }

        PaymentFactory.ApplyStatus(payment, PaymentStatus.Succeeded, DateTime.UtcNow, DateTime.UtcNow);

        // Only a row that was already in the store is marked as updated: Update() on a row that
        // was just Added turns the insert into an update of a row that does not exist yet, and
        // the context does not track by default, so a loaded row has to be marked explicitly.
        if (!isNew)
            _context.Payments.Update(payment);
    }
}
