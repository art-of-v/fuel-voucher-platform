namespace FuelFlow.Features.Vouchers.Movements;

/// <summary>
/// One movement of one piece of paper: it was in one place, and now it is in another. The append-only
/// custody log D-13 exists to build, replacing <c>voucher_renewal_items</c> (dissolved 2026-10-17),
/// <c>voucher_exchanges</c> and <c>operator_voucher_renewals</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A movement is a change of place, and place is not a voucher.</b> The first draft of this table
/// used <c>from_voucher_id</c> / <c>to_voucher_id</c>, which quietly asked two questions with two columns:
/// <i>which paper moved</i> and <i>which paper it became</i>. The second is provenance, not movement, so
/// it was always empty for a real movement — every handover out of the warehouse wrote
/// <c>to_voucher_id = NULL</c> and carried its actual destination in <c>order_id</c>. A column that is
/// NULL whenever the row means what it says is a column modelling the wrong thing.
/// </para>
/// <para>
/// So the paper is <see cref="VoucherId"/>, and the movement is <see cref="FromLocation"/> →
/// <see cref="ToLocation"/>. Every row says where the paper went, with no exceptions and no NULL to
/// interpret.
/// </para>
/// <para>
/// <b>Extend is not here, and does not need a constraint to keep it out.</b> Pushing an expiry out
/// changes no place, so it is not a movement — it is a <c>voucher_events</c> state change. Under the
/// old shape this had to be enforced by refusing <c>from = to</c>, which is a rule about the wrong
/// thing; here it follows from the meaning of the word. TARGET_SCHEMA's original draft listed
/// <c>renewal_extend</c> as a kind and then argued in the same paragraph that it is not a movement.
/// The argument was the correct one.
/// </para>
/// <para>
/// <b>Provenance is derived, not stored.</b> "Which paper did this one come from" is answerable from the
/// movements — an <see cref="VoucherMovementKinds.ExchangeIn"/> and the
/// <see cref="VoucherMovementKinds.ExchangeOut"/> sharing a batch, or a
/// <see cref="VoucherMovementKinds.ReplaceOut"/> pointing at the line that names the old paper — and
/// a <c>voucher_lineage</c> table would be a second copy of that fact to keep in step. There is exactly
/// one place that knows the rule.
/// </para>
/// <para>
/// <b>No <c>xmin</c> concurrency token</b>, matching <c>movement_batches</c>: a movement is written once
/// and never updated. The first thing that updates one should first decide it needs a token.
/// </para>
/// </remarks>
public sealed class VoucherMovement
{
    public Guid Id { get; set; }

    /// <summary>
    /// The settlement this movement is part of, when there was one. Null for a movement an operator or a
    /// customer performed on its own.
    /// </summary>
    public Guid? BatchId { get; set; }

    /// <summary>What happened. See <see cref="VoucherMovementKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The piece of paper that moved.</summary>
    public Guid VoucherId { get; set; }

    /// <summary>Where it was. See <see cref="VoucherLocations"/>.</summary>
    public string FromLocation { get; set; } = string.Empty;

    /// <summary>
    /// Where it is now. Never equal to <see cref="FromLocation"/> — that is
    /// <c>ck_voucher_movements_distinct_locations</c>, and a row that says a paper moved from the shelf
    /// to the shelf is not a movement.
    /// </summary>
    public string ToLocation { get; set; } = string.Empty;

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

    /// <summary>Who caused it: a person, or the scheduler. See <see cref="VoucherActorKinds"/>.</summary>
    public string ActorKind { get; set; } = VoucherActorKinds.User;

    /// <summary>
    /// The person who caused it, required when <see cref="ActorKind"/> is <c>user</c>. Bare uuid, no
    /// foreign key, for the reason every other audit record in this schema gives — see §G of TARGET_SCHEMA,
    /// and <c>movement_batches.opened_by_user_id</c> for a settlement record that already works this way.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The company that caused the movement, when it was a company rather than a user. Bare uuid; D-02.</summary>
    public Guid? ActorPartyId { get; set; }

    /// <summary>
    /// What the customer paid for this movement, in UAH. Null when money did not move — most movements
    /// are the operator's own housekeeping.
    /// </summary>
    /// <remarks>
    /// <c>numeric(12,2)</c> to match <c>orders.price</c> and <c>order_line_items.line_total</c>, whose
    /// per-line amounts this has to be comparable with in a report. It is not the batch total: a
    /// settlement's total lives on <c>movement_batches.surcharge_uah</c>.
    /// </remarks>
    public decimal? AmountUah { get; set; }

    /// <summary>
    /// What this movement did to supplier-side cost, in UAH. Null when cost did not move.
    /// </summary>
    /// <remarks>
    /// Recorded <b>at the time</b>, from the values written in the same transaction, because it cannot
    /// be derived afterwards: <c>fuel_vouchers.cost_per_liter</c> is mutable and
    /// <c>SetBatchCostCommandHandler</c> overwrites it. "What did this voucher cost before" stops
    /// existing the moment the column is restamped, so the only way to keep it is to write it down
    /// while it is true.
    /// </remarks>
    public decimal? CostDeltaUah { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// The paper that took this voucher's place, when this paper was traded for another one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A movement says where a piece of paper went; this says which piece of paper it became. It is
    /// the pairing <c>voucher_exchanges</c> kept as <c>old_voucher_id</c> → <c>new_voucher_id</c>, folded
    /// into the ledger (planning #319) so the ledger can answer "what replaced this voucher" without
    /// the old table beside it.
    /// </para>
    /// <para>
    /// The row that carries it is the row of the <b>retired</b> paper — the one whose story ends here.
    /// The successor's row is the beginning of its own story and carries nothing about the trade. So
    /// the operator's question "what happened to THIS voucher" is answered by reading THAT voucher's
    /// own movements, which is what the lifecycle drawer does.
    /// </para>
    /// <para>
    /// NULL for every movement that was not a trade: a sale, a return, an import. It is not NULL for
    /// every movement that means what it says — a movement without a counterpart is a movement that
    /// moved no paper into another paper, and that is a fact about the world.
    /// </para>
    /// </remarks>
    public Guid? ReplacedByVoucherId { get; set; }

    /// <summary>
    /// This movement's share of the money a settlement moved: the допlata the operator paid the
    /// supplier for this pair of paper.
    /// </summary>
    /// <remarks>
    /// The settlement's total lives on <c>movement_batches.surcharge_uah</c>, and the shares can sum to
    /// less than it — so the total is not this column's value, and this column is not the total.
    /// <c>voucher_exchanges.surcharge_uah</c> held exactly this per-pair share, and the three readers
    /// that summed it per batch and per fuel still need it.
    /// </remarks>
    public decimal? SurchargeShareUah { get; set; }

    /// <summary>
    /// The cost per litre stamped on the replacement when it arrived, recorded at the time.
    /// </summary>
    /// <remarks>
    /// A level, not a delta: what the new paper cost per litre. It cannot be read back later —
    /// <c>fuel_vouchers.cost_per_liter</c> is mutable and <c>SetBatchCostCommandHandler</c> restamps
    /// it, so "what did this paper cost when it arrived" stops existing the moment the column is
    /// restamped. <c>voucher_exchanges.cost_per_liter_applied</c> held exactly this.
    /// </remarks>
    public decimal? AppliedCostPerLiter { get; set; }

    /// <summary>Why, in the operator's words.</summary>
    public string? Reason { get; set; }

    /// <summary>The order line this movement settled, for the renewal paths.</summary>
    public Guid? LineItemId { get; set; }
}

/// <summary>
/// Where a piece of paper can be. The closed set of <see cref="VoucherMovement.FromLocation"/> and
/// <see cref="VoucherMovement.ToLocation"/>.
/// </summary>
/// <remarks>
/// Small on purpose, and it is the vocabulary the business already uses: a ticket is in our stock, in a
/// customer's hands, with a supplier, in a company pool, or in a worker's hands. Everything finer —
/// which customer, which order — is a column on the movement, not a place.
/// </remarks>
public static class VoucherLocations
{
    /// <summary>FuelFlow's own stock. The only location a voucher can be in and leave again.</summary>
    public const string Pool = "pool";

    /// <summary>A customer's hands, reached through an order. <c>order_id</c> says whose.</summary>
    public const string Customer = "customer";

    /// <summary>Handed over to, or received from, a supplier.</summary>
    public const string Supplier = "supplier";

    /// <summary>A company's pool, to be handed to a worker.</summary>
    public const string Company = "company";

    /// <summary>A worker's hands, after a company gift.</summary>
    public const string Worker = "worker";

    public static readonly IReadOnlyList<string> All = [Pool, Customer, Supplier, Company, Worker];
}

/// <summary>The closed set of <see cref="VoucherMovement.Kind"/>, which is exactly a change of place.</summary>
/// <remarks>
/// Eight words, and every one of them moves a piece of paper between two of
/// <see cref="VoucherLocations"/>. A redemption, a block, an activation and an expiry are all one row
/// updated in place — they change what a ticket <i>is</i>, not where it <i>is</i>, and they belong to
/// <c>voucher_events</c>.
/// </remarks>
public static class VoucherMovementKinds
{
    /// <summary>Paper arrived: from the supplier, into our stock.</summary>
    public const string Deposit = "deposit";

    /// <summary>Out of our stock, into a customer's hands under an order.</summary>
    public const string Issued = "issued";

    /// <summary>Out of a customer's hands, back into our stock.</summary>
    public const string Returned = "returned";

    /// <summary>A replacement left our stock for a customer who could not extend in place.</summary>
    /// <remarks>
    /// The old paper going back to the shelf is a <see cref="Returned"/> row of its own, and the two are
    /// two statements in the code — one row for the pair would have recorded one of them.
    /// </remarks>
    public const string ReplaceOut = "replace_out";

    /// <summary>Our stock handed over to a supplier.</summary>
    public const string ExchangeOut = "exchange_out";

    /// <summary>Paper received back from a supplier.</summary>
    public const string ExchangeIn = "exchange_in";

    /// <summary>A company pool handed a voucher to a worker.</summary>
    public const string Gift = "gift";

    /// <summary>A worker hand-off revoked, back to the company pool.</summary>
    public const string Recall = "recall";

    public static readonly IReadOnlyList<string> All =
    [
        Deposit, Issued, Returned, ReplaceOut, ExchangeOut, ExchangeIn, Gift, Recall,
    ];
}
