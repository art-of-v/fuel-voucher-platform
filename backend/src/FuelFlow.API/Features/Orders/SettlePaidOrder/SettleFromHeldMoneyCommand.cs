using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Pay this order out of the money we already hold for the customer, if one payment of exactly its
/// price is sitting unattached.
/// </summary>
/// <remarks>
/// <para>
/// Trigger 2 of planning #867. The settlement channel already settles the case where the money
/// arrives second (it looks for an open order when a message is declared) and the panel settles the
/// case where an operator places the money by hand. Neither of them can help the case that started
/// the incident: the customer pressed Pay, the money had been declared days earlier, and the order
/// waited for an invoice the customer was never going to pay. Until this command existed the only
/// remedy was a person watching the unallocated tab.
/// </para>
/// <para>
/// It is asked for by a path that has just written an order, and it answers whether that order is
/// paid. It is not a general "does this customer have money": the match is one payment, for exactly
/// this price, belonging to exactly this customer, attached to nothing.
/// </para>
/// </remarks>
public sealed record SettleFromHeldMoneyCommand
{
    /// <summary>The order to pay, which must still be waiting for its payment.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// What is asking, in the words an operator would use. Logged with the settle, because money that
    /// moved without the acquirer saying so has to be traceable to the path that moved it.
    /// </summary>
    public required string Because { get; init; }
}

/// <summary>
/// Why held money was not used, as something the caller can branch on rather than re-derive.
/// </summary>
public enum HeldMoneyOutcome
{
    /// <summary>
    /// Nothing is held for this customer at this price. The normal answer, and the one most checkouts
    /// get: the customer simply has not paid yet.
    /// </summary>
    NothingHeld,

    /// <summary>
    /// More than one unattached payment matches. Which one pays for this order is not something to
    /// guess, and the same rule the settlement channel applies when a customer has two open orders.
    /// An operator decides.
    /// </summary>
    SeveralToChoose,

    /// <summary>The order is not in a state money can pay for, so nothing was attached.</summary>
    OrderNotWaiting,

    /// <summary>
    /// The money was attached and the settle core moved the order. The acquirer must not be asked for
    /// an invoice now: an invoice is a request to be charged, and this customer has already been.
    /// </summary>
    Settled,

    /// <summary>
    /// The money was attached and the core still declined, for the reason in
    /// <see cref="SettleFromHeldMoneyResult.Reason"/>. The payment keeps the order it is now attached
    /// to: money that arrived for a sale belongs on that sale whether or not it also settled it.
    /// </summary>
    AttachedButDeclined,
}

/// <summary>
/// What happened to the held money, and which payment row is now the evidence.
/// </summary>
public sealed record SettleFromHeldMoneyResult
{
    public required HeldMoneyOutcome Outcome { get; init; }

    /// <summary>The payment that paid for it, when one was attached.</summary>
    public Guid? PaymentId { get; init; }

    /// <summary>A sentence safe to show a human, when the order did not settle.</summary>
    public string? Reason { get; init; }

    public bool Settled => Outcome == HeldMoneyOutcome.Settled;

    public static SettleFromHeldMoneyResult Nothing() =>
        new() { Outcome = HeldMoneyOutcome.NothingHeld };

    public static SettleFromHeldMoneyResult Declined(HeldMoneyOutcome outcome, string reason) =>
        new() { Outcome = outcome, Reason = reason };

    public static SettleFromHeldMoneyResult PaidBy(Guid paymentId) =>
        new() { Outcome = HeldMoneyOutcome.Settled, PaymentId = paymentId };
}
