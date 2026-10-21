namespace FuelFlow.Features.Vouchers.Exchange;

/// <summary>
/// One operator-initiated settlement that moves more than a single voucher — the parent the
/// exchange's ledger rows point at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists at all.</b> The exchange's batch id has been an indexed uuid pointing at
/// nothing since the column was created on 2026-10-01. The schema presented it as a
/// reference to a thing that did not exist, so a batch's lifecycle — who opened it, when, whether it
/// was ever confirmed, and what was actually agreed with the supplier — had nowhere to live. Every
/// reader reconstructed the header by folding over the member rows: <c>MIN(created_at_utc)</c> for
/// the time, "the first non-null name" for the operator, <c>SUM</c> for the money. That reconstruction
/// is an assumption, not a fact, and three separate call sites made it with two different grains.
/// </para>
/// <para>
/// <b>What is stored here and what is still derived.</b> This table holds the facts that belong to the
/// settlement as a whole: which supplier, what was agreed, who did it, when, and its lifecycle state.
/// Per-pair facts stay on the pair — which fuels it touched, how many
/// vouchers moved, the litres-weighted cost before. Those are genuinely a property of the pairs, and
/// duplicating them here would create a second thing to keep in step. The line is: <i>a fact about
/// the batch</i> is stored; <i>a fact about a pair</i> is stored on the pair.
/// </para>
/// <para>
/// <b>Deliberately narrower than TARGET_SCHEMA's D-13.</b> The roadmap names
/// <c>supplier_party_id uuid REFERENCES legal_entities(party_id)</c> and a foreign-key'd
/// <c>confirmed_by_user_id</c>. Neither is buildable yet: <c>parties</c> is D-02 and does not exist,
/// and the actor-column rule in §G of that same document says audit actor columns stay FK-free because
/// a settlement record that refuses to be written over a missing actor row is worse than a row
/// carrying an id nobody can resolve. This table follows the rule the codebase already follows for
/// <c>operator_voucher_renewals.acting_user_id</c>. When
/// D-02 lands, the supplier column is an additive change.
/// </para>
/// <para>
/// <b>No <c>xmin</c> concurrency token, unlike its three siblings.</b> A batch is written once, in the
/// same transaction as the exchange rows that point at it, and nothing updates it today — a status
/// transition would be the first thing that did, and that is when it should get a token rather than
/// carrying one for a hypothetical writer.
/// </para>
/// </remarks>
public sealed class MovementBatch
{
    public Guid Id { get; set; }

    /// <summary>What kind of settlement this is. See <see cref="MovementBatchKinds"/>.</summary>
    public string Kind { get; set; } = MovementBatchKinds.VoucherExchange;

    /// <summary>The counterparty. Null for a batch that is not a supplier settlement.</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>
    /// The lifecycle state. See <see cref="MovementBatchStatuses"/>.
    /// </summary>
    /// <remarks>
    /// A batch is created already confirmed, because that is the only way the application makes one:
    /// the operator confirms the exchange and the batch records it in the same transaction. The rest
    /// of the set is the forward shape — a draft that is opened and later settled is the obvious next
    /// workflow, and the CHECK is what stops an unrecognised word arriving as a status later.
    /// </remarks>
    public string Status { get; set; } = MovementBatchStatuses.Confirmed;

    /// <summary>
    /// What the brand charged for the settlement as a whole.
    /// </summary>
    /// <remarks>
    /// <b>This is the amount on the command, not the sum of the rows' shares.</b> Each
    /// pair of paper carries only its own share, computed as
    /// <c>surcharge × pair_litres / total_new_litres</c>, and those shares sum back to the total only
    /// when every retired voucher was paired with a replacement. An exchange that returns vouchers
    /// without reissuing them — a legitimate, supported case — contributes retired litres to the
    /// numerators of the paired rows and nothing to the denominator, so the shares over-count. The
    /// number the brand charged is the authoritative one, so it is this.
    /// </remarks>
    public decimal SurchargeUah { get; set; }

    /// <summary>The operator who opened the settlement.</summary>
    /// <remarks>
    /// Bare uuid, with no foreign key, for the reason every other settlement record in this schema
    /// gives: see <see cref="MovementBatch"/>.
    /// </remarks>
    public Guid OpenedByUserId { get; set; }

    /// <summary>
    /// That operator's name as it read at the time.
    /// </summary>
    /// <remarks>
    /// A denormalised copy so the record stays readable after a rename, never a join target — the same
    /// reasoning every other audit row in this schema follows.
    /// </remarks>
    public string? OpenedByUserName { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    /// <summary>
    /// When the settlement was confirmed. Non-null exactly when
    /// <see cref="Status"/> is <c>confirmed</c>, which is a database CHECK rather than a convention:
    /// a batch that says confirmed and carries no timestamp cannot answer "when", and one that
    /// carries a timestamp without saying confirmed is asserting something nobody decided.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>Why a batch was rejected, when it was.</summary>
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>The closed set of <see cref="MovementBatch.Kind"/>.</summary>
public static class MovementBatchKinds
{
    /// <summary>A supplier settlement of retired vouchers. The only kind written today.</summary>
    public const string VoucherExchange = "voucher_exchange";

    /// <summary>
    /// A single import released to many vouchers at once. Not written yet; present so the CHECK is a
    /// closed set rather than an open one that quietly accepts anything.
    /// </summary>
    public const string BulkIssuance = "bulk_issuance";

    public static readonly IReadOnlyList<string> All = [VoucherExchange, BulkIssuance];
}

/// <summary>The closed set of <see cref="MovementBatch.Status"/>.</summary>
public static class MovementBatchStatuses
{
    public const string Draft = "draft";
    public const string Confirmed = "confirmed";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, Confirmed, Rejected, Cancelled];
}