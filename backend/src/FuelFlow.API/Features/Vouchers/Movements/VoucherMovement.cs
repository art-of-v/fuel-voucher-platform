namespace FuelFlow.Features.Vouchers.Movements;

/// <summary>
/// One movement of one voucher: where it came from, where it went, who did it, when, and what it
/// cost. The single append-only record D-13 exists to build, replacing
/// <c>voucher_renewal_items</c> (dissolved 2026-10-17), <c>voucher_exchanges</c> and
/// <c>operator_voucher_renewals</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A movement is a fact about a voucher, not about a payment.</b> The three tables this replaces
/// each answered "which voucher went where" and each also stored whatever else its writer happened to
/// know, which is why they disagreed on shape. This one stores the movement and nothing else: what
/// was bought is an <c>orders</c>/<c>order_line_items</c> fact, what a voucher cost is a
/// <c>cost_delta_uah</c> on the row that moved it, and who the customer is is <c>to_voucher_id</c>'s
/// business, not this row's.
/// </para>
/// <para>
/// <b>Extend is deliberately absent from <see cref="Kinds"/>.</b> Pushing a voucher's expiry out moves
/// no voucher — <c>from</c> and <c>to</c> would be the same row, which
/// <c>ck_voucher_movements_distinct_sides</c> forbids. It is a state change and belongs in
/// <c>voucher_events</c>; the money the customer paid is already recorded against the renewal order
/// line. The TARGET_SCHEMA draft listed <c>renewal_extend</c> as a kind and then argued in the same
/// paragraph that it is not a movement, and the argument is the one that survived.
/// </para>
/// <para>
/// <b>A replacement is two movements, not one.</b> <c>FulfillmentService</c> claims the replacement out
/// of stock and returns the customer's old voucher in two separate statements, so they are
/// <see cref="Kinds.ReplaceOut"/> and <see cref="Kinds.Restock"/>. One row for the pair would have
/// recorded one of them.
/// </para>
/// <para>
/// <b>No <c>xmin</c> concurrency token</b>, matching <c>movement_batches</c> and for the same reason:
/// a movement is written once and never updated. The first thing that updates one is the thing that
/// should first decide it needs a token.
/// </para>
/// </remarks>
public sealed class VoucherMovement
{
    public Guid Id { get; set; }

    /// <summary>
    /// The settlement this movement is part of, when there was one. Null for a movement an operator or
    /// a customer performed on its own.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>What happened. See <see cref="VoucherMovementKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The voucher the movement started from. Null for a <see cref="VoucherMovementKinds.Deposit"/>,
    /// which is the only kind with nothing before it.
    /// </summary>
    /// <remarks>
    /// Nullable on purpose, and the single most consequential column decision here.
    /// <c>ImportVouchersCommand</c> creates the majority of vouchers in this system, and a voucher
    /// arriving from a supplier PDF has no predecessor to name. Under <c>NOT NULL</c> — which is what
    /// TARGET_SCHEMA first proposed — none of them was recordable, and a ledger that cannot record
    /// arrival cannot answer "where did this paper come from".
    /// </remarks>
    public Guid? FromVoucherId { get; set; }

    /// <summary>The voucher the movement ended at. Null when the voucher left the system or changed hands in place.</summary>
    public Guid? ToVoucherId { get; set; }

    /// <summary>
    /// The order this movement settled, when a customer paid for it. Bare uuid, no foreign key.
    /// </summary>
    /// <remarks>
    /// An audit row must not be the thing that blocks deleting its own subject.
    /// <c>order_line_items.order_id</c> is RESTRICT, and that made
    /// <c>DeletedUnpaidOrderCleanupService</c> remove tracked lines before the order. The same FK here
    /// would either wedge that job again or force it to delete ledger rows, which is worse: a ledger
    /// that gets deleted is not a ledger.
    /// </remarks>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// The operator or customer who caused the movement. Bare uuid, no foreign key, for the reason
    /// every other audit record in this schema gives — see §G of TARGET_SCHEMA, and
    /// <c>movement_batches.opened_by_user_id</c> for a settlement record that already works this way.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The company that caused the movement, when it was a company rather than a user. Bare uuid; D-02.</summary>
    public Guid? ActorPartyId { get; set; }

    /// <summary>
    /// What the customer paid for this movement, in UAH. Null when money did not move — most
    /// movements are the operator's own housekeeping.
    /// </summary>
    /// <remarks>
    /// <c>numeric(12,2)</c> to match <c>orders.price</c> and <c>order_line_items.line_total</c>, whose
    /// per-line amounts this has to be comparable with in a report. It is not the batch total: a
    /// settlement's total lives on <c>movement_batches.surcharge_uah</c>, and a voucher's share of it
    /// is here.
    /// </remarks>
    public decimal? AmountUah { get; set; }

    /// <summary>
    /// What this movement did to supplier-side cost, in UAH. Null when cost did not move.
    /// </summary>
    /// <remarks>
    /// Recorded <b>at the time</b>, from the values being written in the same transaction, because it
    /// cannot be derived afterwards: <c>fuel_vouchers.cost_per_liter</c> is mutable and
    /// <c>SetBatchCostCommandHandler</c> overwrites it. "What did this voucher cost before" stops
    /// existing the moment the column is restamped, so the only way to keep it is to write it down
    /// while it is true. This is the same defect class the renewal expiry snapshots exist to prevent.
    /// </remarks>
    public decimal? CostDeltaUah { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Why, in the operator's words.
    /// </summary>
    /// <remarks>
    /// Also what distinguishes the cases <see cref="VoucherMovementKinds.Restock"/> deliberately
    /// covers: a quantity-mismatch release and a replaced customer's voucher going back to the shelf
    /// are different events that want the same kind. Splitting the enum to describe every nuance
    /// would make <see cref="Kind"/> a second copy of this column that can drift from it.
    /// </remarks>
    public string? Reason { get; set; }

    /// <summary>The order line this movement settled, for the renewal paths.</summary>
    public Guid? LineItemId { get; set; }
}

/// <summary>The closed set of <see cref="VoucherMovement.Kind"/>.</summary>
/// <remarks>
/// Named after what the code calls these things, not after what the first draft of D-13 called them.
/// There is no <c>freeze</c>: <c>FuelVoucher.Status</c> has never had a frozen state, it has
/// <c>Blocked</c>. There is no <c>renewal_extend</c>: see <see cref="VoucherMovement"/>.
/// </remarks>
public static class VoucherMovementKinds
{
    /// <summary>Vouchers arrived from a supplier PDF. The only kind with no predecessor.</summary>
    public const string Deposit = "deposit";

    public const string Activate = "activate";
    public const string Deactivate = "deactivate";
    public const string Archive = "archive";
    public const string Unarchive = "unarchive";

    /// <summary>Went to a holder: the buy-fuel sale.</summary>
    public const string Issuance = "issuance";

    /// <summary>Spent at a pump.</summary>
    public const string Redeem = "redeem";

    /// <summary>A redemption was undone.</summary>
    public const string Unredeem = "unredeem";

    /// <summary>Stock handed over to the supplier in an exchange.</summary>
    public const string ExchangeOut = "exchange_out";

    /// <summary>New paper received back from the supplier.</summary>
    public const string ExchangeIn = "exchange_in";

    /// <summary>A replacement claimed out of FuelFlow's own stock for a renewal.</summary>
    public const string ReplaceOut = "replace_out";

    /// <summary>Back into stock from a holder — a release, or a replaced customer's voucher.</summary>
    public const string Restock = "restock";

    /// <summary>A company pool handing a voucher to a worker.</summary>
    public const string Gift = "gift";

    /// <summary>A worker hand-off revoked.</summary>
    public const string Recall = "recall";

    public const string Block = "block";
    public const string Unblock = "unblock";

    /// <summary>
    /// A worker was dismissed. Distinct from <see cref="Unblock"/>: there the voucher returns to the
    /// pool, here it stays blocked.
    /// </summary>
    public const string FireWorker = "fire_worker";

    /// <summary>The voucher's real term ran out, or it was written off as a loss.</summary>
    public const string Expire = "expire";

    public static readonly IReadOnlyList<string> All =
    [
        Deposit, Activate, Deactivate, Archive, Unarchive, Issuance, Redeem, Unredeem,
        ExchangeOut, ExchangeIn, ReplaceOut, Restock, Gift, Recall, Block, Unblock, FireWorker, Expire,
    ];
}
