using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Monobank.ProcessWebhook;

/// <summary>
/// Guarded state machine for order status transitions.
/// Webhooks may only move an order from a payable state toward fulfillment or cancellation;
/// terminal states (Fulfilled, Cancelled, Refunded) are immutable from the outside.
/// </summary>
public static class OrderStateMachine
{
    public static bool CanTransition(OrderStatus current, OrderStatus target)
    {
        return current switch
        {
            OrderStatus.PendingPayment or OrderStatus.Paid =>
                target is OrderStatus.PendingFulfillment or OrderStatus.Cancelled,
            _ => false
        };
    }

    public static bool IsTerminal(OrderStatus status)
    {
        return status is OrderStatus.Fulfilled or OrderStatus.Cancelled
            or OrderStatus.Refunded or OrderStatus.PartiallyRefunded;
    }
}
