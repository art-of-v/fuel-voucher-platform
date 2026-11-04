using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Settle the order if, and only if, the money on it pays for it.
/// </summary>
/// <remarks>
/// <para>
/// The difference between this and <see cref="SettlePaidOrderCommand"/> is who has already decided.
/// <c>SettlePaidOrderCommand</c> is called by an authority that has just proved the money arrived —
/// the acquirer's <c>success</c>, checked against the price and the signature. This command is called
/// by a path that holds money and an order and has to work out whether they are the same transaction:
/// the settlement channel attaching a declared payment, an operator placing money in the panel, an
/// order whose held money turns up later. Those callers must not have to know the settle's rules, and
/// they must not be able to skip them either, so the check lives here.
/// </para>
/// <para>
/// Declining is a normal answer. The caller is told why in words, because every one of these paths
/// ends somewhere a human reads — a Telegram reply, a panel refusal, a log line about money that
/// arrived and did not buy anything.
/// </para>
/// </remarks>
public sealed record TrySettlePaidOrderCommand
{
    /// <summary>The order to settle, if its money covers it.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// What is asking, in the words an operator would use ("the settlement channel attached
    /// 1504.00 ₴"). Logged with the settle, because money that moved without an acquirer saying so is
    /// the one event an investigator needs to be able to trace back to a person or a message.
    /// </summary>
    public required string Because { get; init; }
}

/// <summary>
/// Why a try-settle did not settle, as something a caller can branch on.
/// </summary>
/// <remarks>
/// The sentence alone cannot be branched on, and a caller that re-derives the reason for itself — the
/// panel asking whether an order is terminal, for instance — builds a second copy of the rule that
/// <see cref="OrderStateMachine"/> exists to keep in one place. These are the guard's own answers, so
/// the caller decides what to tell the human without deciding why.
/// </remarks>
public enum SettleRefusal
{
    /// <summary>
    /// The order is where a settle would put it and the handover is already queued. Nothing to do, and
    /// not a failure: a redelivered message or a second press lands here.
    /// </summary>
    AlreadyHandedOver,

    /// <summary>
    /// The order is in a state that money cannot settle — terminal, or part-delivered. A person has to
    /// act somewhere else, and the money's own position is the news.
    /// </summary>
    OrderRefusesSettlement,

    /// <summary>
    /// The money on the order is real but does not cover its price. Held and waiting is the correct
    /// state for it, not a refusal to fix.
    /// </summary>
    MoneyIsShort,

    /// <summary>No order with that id, visible or hidden.</summary>
    NoSuchOrder,
}

/// <summary>
/// Whether the order was settled, and if it was not — which of the guard's answers it was, and why in
/// words.
/// </summary>
/// <remarks>
/// <see cref="Refusal"/> is the only fact here; <c>Settled</c> and <see cref="Reason"/> read from it, so
/// an answer cannot say "not settled" while naming no reason.
/// </remarks>
public sealed record TrySettlePaidOrderResult
{
    public SettleRefusal? Refusal { get; init; }

    /// <summary>Why it did not, in a sentence safe to show a human.</summary>
    public string? Reason { get; init; }

    public bool Settled => Refusal is null;

    public static TrySettlePaidOrderResult Done() => new();

    public static TrySettlePaidOrderResult Declined(SettleRefusal refusal, string reason) =>
        new() { Refusal = refusal, Reason = reason };
}
