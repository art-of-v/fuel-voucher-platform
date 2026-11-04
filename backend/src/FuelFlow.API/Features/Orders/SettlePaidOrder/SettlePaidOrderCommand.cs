using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// The order has been paid. Move it to fulfilment and book the money.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of a payment webhook that is not about the acquirer. The webhook, the
/// reconciliation poller and, once planning #867 lands, the settlement channel all answer the same
/// question - "has this order's money arrived?" - and then do the same four things about it:
/// transition the order, restore it if it had been hidden, hand it to fulfilment, post the receipt.
/// Those four are one unit of work and they are where the money enters the books, so they live in
/// one handler that every authority calls, rather than in each caller.
/// </para>
/// <para>
/// What this command deliberately does NOT carry: the provider's status, its modified date, its
/// amount, or its signature. Those are the acquirer's evidence and belong to the caller, which is
/// the only thing that can say what it observed. The columns that record them
/// (<c>monobank_status</c>, <c>last_webhook_*>) are set by the acquirer's own path before it calls
/// here, and are saved by the one <c>SaveChanges</c> this handler makes - so an authority that
/// never spoke to an acquirer cannot leave acquirer marks behind.
/// </para>
/// </remarks>
public sealed record SettlePaidOrderCommand
{
    /// <summary>
    /// The order whose money has arrived, in a state the caller has already accepted
    /// (<c>PendingPayment</c>, or a soft-deleted <c>PendingPayment</c> that was paid anyway).
    /// </summary>
    /// <remarks>
    /// The tracked entity, not an id, and it must arrive with <c>LineItems</c> loaded: the outbox
    /// payload names the first line's brand, fuel type and litres, and a caller that saved the
    /// order under a global filter excluding soft-deleted rows would not be able to settle one at
    /// all. The caller loads it because the caller is what applied its own guards to it - a
    /// replayed webhook, a stale callback, an amount that disagrees.
    /// </remarks>
    public required Order Order { get; init; }
}
