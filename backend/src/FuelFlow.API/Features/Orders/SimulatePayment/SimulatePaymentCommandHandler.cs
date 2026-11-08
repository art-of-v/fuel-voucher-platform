using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
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
    private readonly SettlePaidOrderCommandHandler _settle;
    private readonly ILogger<SimulatePaymentCommandHandler> _logger;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public SimulatePaymentCommandHandler(
        ApplicationDbContext context,
        GetUserPurchasesCommandHandler getUserPurchasesHandler,
        SettlePaidOrderCommandHandler settle,
        ILogger<SimulatePaymentCommandHandler> logger,
        IBackgroundJobClient backgroundJobClient)
    {
        _context = context;
        _getUserPurchasesHandler = getUserPurchasesHandler;
        _settle = settle;
        _logger = logger;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<SimulatePaymentResponse> HandleAsync(
        SimulatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        // IncludingSoftDeleted and LineItems, for the same two reasons the webhook loads its order
        // that way: a paid order that had been swipe-deleted must settle and come back into view,
        // and the ORDER_CREATED payload names the first line's brand, fuel type and litres.
        var order = await _context.Orders
            .IncludingSoftDeleted()
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);

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
            order.UpdatedAtUtc = SystemClock.UtcNow;
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

            // What the acquirer would have observed, on the acquirer's own column - the same mark
            // the real webhook sets before it hands the order to the settle core.
            order.MonobankStatus = MonobankStatus.Success;

            // The money row follows the order, exactly as the real webhook does it: the acquirer
            // says the money moved, and the payment record says so too. It used to be left at
            // Pending, which left the simulation claiming a payment that the ledger said had not
            // happened - and, now that fulfilment asks the money row whether the order was paid
            // for, it would have refused the order the simulation had just paid for.
            await MarkCaptureAsSucceededAsync(order, cancellationToken);

            // The transition, the hidden-order restore, the ORDER_CREATED handover and the receipt
            // are the settle core's, not a copy kept here. The simulation used to write three of
            // those itself and book nothing, so a paid order in Development was invisible to the
            // revenue report; one authority answers "the money arrived", whichever path found out.
            await _settle.HandleAsync(new SettlePaidOrderCommand { Order = order }, cancellationToken);

            // A renewal order fulfils on its own path (extend/replace existing vouchers): the core
            // deliberately writes no ORDER_CREATED for it, and it is handed to
            // ProcessRenewalOrderAsync exactly as the real webhook hands one.
            if (order.Kind == OrderKind.Renewal)
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
                SystemClock.UtcNow);
            _context.Payments.Add(payment);
        }

        PaymentFactory.ApplyStatus(payment, PaymentStatus.Succeeded, SystemClock.UtcNow, SystemClock.UtcNow);

        // Only a row that was already in the store is marked as updated: Update() on a row that
        // was just Added turns the insert into an update of a row that does not exist yet, and
        // the context does not track by default, so a loaded row has to be marked explicitly.
        if (!isNew)
            _context.Payments.Update(payment);
    }
}
