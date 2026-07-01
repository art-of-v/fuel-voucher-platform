namespace FuelFlow.Features.Orders.SharedModels;

public enum OrderStatus
{
    PendingPayment,
    Paid,
    PendingFulfillment,
    PartiallyFulfilled,
    Fulfilled,
    Refunded,
    Cancelled
}
