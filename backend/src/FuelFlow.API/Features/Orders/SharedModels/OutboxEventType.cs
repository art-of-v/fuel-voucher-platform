namespace FuelFlow.Features.Orders.SharedModels;

public enum OutboxEventType
{
    OrderCreated,
    OrderFulfilled,
    OrderCancelled,
    PaymentCompleted,
    VoucherExpired,
    VoucherUsed,
    VoucherActivated
}
