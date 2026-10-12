namespace FuelFlow.Features.Orders.SharedModels;

public enum OutboxEventType
{
    OrderCreated,
    OrderFulfilled,
    OrderCancelled,
    PaymentCompleted,
    VoucherExpired,
    VoucherUsed,
    VoucherActivated,

    /// <summary>
    /// A company owner issued fuel to a worker (#150 W3). Distinct from <c>OrderFulfilled</c>: that
    /// one is about a customer's own purchase, this is about fuel handed to somebody at work - which
    /// is the event that should reach a worker's phone, because they were not the buyer and have no
    /// order screen of their own to check.
    /// </summary>
    VoucherIssued
}
