using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// Guarded state machine for order status transitions.
/// <para>
/// Webhooks may only move an order from a payable state toward fulfillment or cancellation;
/// terminal states (Fulfilled, Cancelled, Refunded) are immutable from the outside.
/// </para>
/// <para>
/// It used to live in <c>Features/Monobank/ProcessWebhook</c>, which made it look like the
/// acquirer's private rule. It is not: the acquirer's webhook is one of the two things that
/// move an order, and the operator path is the other. Both read the same machine, so the two
/// cannot drift apart - and the defect this file exists to prevent was exactly a drift.
/// </para>
/// </summary>
public static class OrderStateMachine
{
    /// <summary>
    /// What the acquirer may do to an order. The acquirer's word is the authority here: it says
    /// the money moved (or did not), and the order follows.
    /// </summary>
    public static bool CanTransition(OrderStatus current, OrderStatus target)
    {
        return current switch
        {
            OrderStatus.PendingPayment or OrderStatus.Paid =>
                target is OrderStatus.PendingFulfillment or OrderStatus.Cancelled,
            _ => false
        };
    }

    /// <summary>
    /// What an operator may do to an order by hand.
    /// <para>
    /// Two moves, and only two, because everything else the system does by itself:
    /// </para>
    /// <list type="number">
    ///   <item><b>Abandon an unpaid order</b> — <c>PendingPayment -&gt; Cancelled</c>. Nobody paid,
    ///   so nothing has to be returned.</item>
    ///   <item><b>Hand a paid order to fulfilment</b> — <c>PendingPayment | Paid -&gt;
    ///   PendingFulfillment</c>. This is the recovery path for an order whose webhook never
    ///   arrived, and it is the move that hands out fuel, so it carries the money question:
    ///   <see cref="RequiresPayment"/> says the caller must check that the money is there.</item>
    /// </list>
    /// <para>
    /// A <b>paid</b> order cannot be cancelled by hand: cancelling it would keep the money and
    /// the goods, so that is the refund path's business and nobody else's. A terminal order
    /// cannot be moved at all.
    /// </para>
    /// </summary>
    public static bool CanOperatorTransition(OrderStatus current, OrderStatus target) =>
        (current, target) switch
        {
            (OrderStatus.PendingPayment, OrderStatus.Cancelled) => true,
            (OrderStatus.PendingPayment, OrderStatus.PendingFulfillment) => true,
            (OrderStatus.Paid, OrderStatus.PendingFulfillment) => true,
            _ => false
        };

    /// <summary>
    /// Whether reaching this state means the goods are owed — and therefore that the money has to
    /// be there. An order in one of these states is one the fulfilment job will pick up and hand
    /// vouchers for.
    /// </summary>
    public static bool RequiresPayment(OrderStatus target) =>
        target is OrderStatus.PendingFulfillment or OrderStatus.PartiallyFulfilled;

    /// <summary>
    /// Why a transition was refused, in one sentence. The refusal used to be a bare "false", and
    /// the operator saw "failed to change the order's status" with no reason attached.
    /// </summary>
    public static string DescribeRefusal(OrderStatus current, OrderStatus target)
    {
        if (IsTerminal(current))
            return $"A {current} order is finished and cannot change.";

        if (target is OrderStatus.Cancelled)
            return $"A {current} order cannot be cancelled by hand - a paid order is refunded, not cancelled.";

        return $"An operator cannot move an order from {current} to {target}.";
    }

    public static bool IsTerminal(OrderStatus status)
    {
        return status is OrderStatus.Fulfilled or OrderStatus.Cancelled
            or OrderStatus.Refunded or OrderStatus.PartiallyRefunded;
    }
}
