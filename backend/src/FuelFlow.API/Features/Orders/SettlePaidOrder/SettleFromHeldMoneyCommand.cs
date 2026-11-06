namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Pay this order out of the money we already hold for the customer, if the balance covers what the
/// order still owes.
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
/// paid. It is not a general "does this customer have money": the question is whether what this order
/// still owes is sitting in this customer's balance, and the answer is a draw across however many
/// transfers it takes — or the shortfall, when it does not.
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
    /// Nothing is held for this customer at all. The normal answer, and the one most checkouts
    /// get: the customer simply has not paid yet.
    /// </summary>
    NothingHeld,

    /// <summary>
    /// Money is held and it is not enough. Different from <see cref="NothingHeld"/> because the two
    /// want different sentences: nothing held is an order that can wait for a transfer that has not
    /// arrived, and short money is an account an operator or a customer has to look at and decide
    /// about. The figure is in <see cref="SettleFromHeldMoneyResult.Reason"/> and in
    /// <see cref="SettleFromHeldMoneyResult.ShortfallUah"/>.
    /// </summary>
    BalanceIsShort,

    /// <summary>The order is not in a state money can pay for, so nothing was drawn.</summary>
    OrderNotWaiting,

    /// <summary>
    /// The balance covered what the order owed, the draws were written, and the settle core moved the
    /// order. The acquirer must not be asked for an invoice now: an invoice is a request to be charged,
    /// and this customer has already been.
    /// </summary>
    Settled,

    /// <summary>
    /// The draws were written and the core still declined, for the reason in
    /// <see cref="SettleFromHeldMoneyResult.Reason"/>. They were then taken back: the order did not
    /// become a sale, so nothing had been answered for, and money left in a draw nobody can see the
    /// point of is money missing from a balance for no reason.
    /// </summary>
    DrawRefused,
}

/// <summary>
/// What happened to the held money, and which payment rows are now the evidence.
/// </summary>
public sealed record SettleFromHeldMoneyResult
{
    public required HeldMoneyOutcome Outcome { get; init; }

    /// <summary>
    /// The payments drawn on, oldest first. Several, because one transfer is not one sale and one sale
    /// is not one transfer; empty when the order settled on money that was already on it.
    /// </summary>
    public IReadOnlyList<Guid> PaymentIds { get; init; } = [];

    /// <summary>What the balance did not cover, when it did not cover it.</summary>
    public decimal ShortfallUah { get; init; }

    /// <summary>A sentence safe to show a human, when the order did not settle.</summary>
    public string? Reason { get; init; }

    public bool Settled => Outcome == HeldMoneyOutcome.Settled;

    public static SettleFromHeldMoneyResult Nothing() =>
        new() { Outcome = HeldMoneyOutcome.NothingHeld };

    public static SettleFromHeldMoneyResult Declined(HeldMoneyOutcome outcome, string reason) =>
        new() { Outcome = outcome, Reason = reason };

    public static SettleFromHeldMoneyResult Short(decimal shortfallUah, decimal heldUah, decimal owedUah) =>
        new()
        {
            Outcome = HeldMoneyOutcome.BalanceIsShort,
            ShortfallUah = shortfallUah,
            Reason = FormattableString.Invariant(
                $"The account holds {heldUah:0.00} ₴, this order owes {owedUah:0.00} ₴, so it is {shortfallUah:0.00} ₴ short.")
        };

    public static SettleFromHeldMoneyResult PaidBy(IReadOnlyList<Guid> paymentIds) =>
        new() { Outcome = HeldMoneyOutcome.Settled, PaymentIds = paymentIds };
}
