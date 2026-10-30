using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Events;

/// <summary>
/// One thing that happened to a voucher which did not move it: a status change, an expiry being pushed
/// out, or the soft-delete flag flipping. The other half of D-13's record — <c>voucher_movements</c>
/// holds custody.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists next to the ledger rather than inside it.</b> Almost nothing in this system moves
/// a voucher: one <c>fuel_vouchers</c> row is updated in place for its whole life. Activation, a
/// redemption, a block, handing a ticket to a customer and taking it back are all that same row with
/// different columns set. So the ledger can only answer "where is this paper", and this table answers
/// "what was done to it". Neither derives the other — you cannot tell a customer's expiry moved from a
/// custody row, and you cannot tell who received a ticket from a status row — so each fact has exactly
/// one owner.
/// </para>
/// <para>
/// <b>A handover writes both, and that is not duplication.</b> Issuing a ticket is one moment and two
/// facts about it: the paper went to a customer (a movement) and its status became
/// <c>Assigned</c> (a <see cref="VoucherEventTypes.StatusChanged"/>). Cramming both into one row is
/// what made the first shape of the ledger unusable, because it left a column empty on every real
/// row.
/// </para>
/// <para>
/// <b>The expiry pair exists for the same reason the renewal snapshots do.</b>
/// <c>fuel_vouchers.customer_expiration_date</c> is overwritten in place by a renewal, so "what was it
/// before" is gone unless it is written down at the moment it is still true. Half a pair, or a pair
/// that runs backwards, is worse than none — hence the two CHECKs.
/// </para>
/// <para>
/// <b>No partitioning, deliberately.</b> <c>TARGET_SCHEMA</c> sketches this table as
/// <c>PARTITION BY RANGE (occurred_at_utc)</c>, and a partitioned table needs a job that creates next
/// month's partition — without one it starts rejecting inserts. That job does not exist, production is
/// pre-launch, and a plain table with one index is a year of trouble cheaper to fix.
/// </para>
/// <para>
/// <b>No <c>xmin</c> concurrency token</b>, matching <c>movement_batches</c> and <c>voucher_movements</c>:
/// an event is written once and never updated. A correction is another event.
/// </para>
/// </remarks>
public sealed class VoucherEvent : IAppendOnlyEntity
{
    public Guid Id { get; set; }

    /// <summary>The voucher this happened to.</summary>
    public Guid VoucherId { get; set; }

    /// <summary>What kind of thing happened. See <see cref="VoucherEventTypes"/>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The status before. Set for <see cref="VoucherEventTypes.StatusChanged"/> only.</summary>
    public string? FromStatus { get; set; }

    /// <summary>The status after. Set for <see cref="VoucherEventTypes.StatusChanged"/> only.</summary>
    public string? ToStatus { get; set; }

    /// <summary>The expiry before. Set for <see cref="VoucherEventTypes.Extended"/> only.</summary>
    public DateOnly? PreviousCustomerExpiration { get; set; }

    /// <summary>The expiry after. Set for <see cref="VoucherEventTypes.Extended"/> only.</summary>
    public DateOnly? NewCustomerExpiration { get; set; }

    /// <summary>
    /// What the caller can be pointed back at — an order, a movement, a batch, a command.
    /// </summary>
    /// <remarks>
    /// The same column <c>order_events.external_ref</c> carries. Indexed, because "show me everything
    /// that happened because of order X" is the question that turns an event log from trivia into
    /// evidence.
    /// </remarks>
    public string? ExternalRef { get; set; }

    /// <summary>Who caused it: a person, or the scheduler. See <see cref="VoucherActorKinds"/>.</summary>
    public string ActorKind { get; set; } = VoucherActorKinds.User;

    /// <summary>
    /// The person who caused it, required when <see cref="ActorKind"/> is <c>user</c>. Bare uuid, no
    /// foreign key — see §G of TARGET_SCHEMA.
    /// </summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The company that caused it. Bare uuid; D-02.</summary>
    public Guid? ActorPartyId { get; set; }

    /// <summary>Why, in the operator's words.</summary>
    public string? Reason { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Anything else worth keeping that has no column of its own.</summary>
    public string? Payload { get; set; }
}

/// <summary>The closed set of <see cref="VoucherEvent.EventType"/>.</summary>
/// <remarks>
/// Five words, and deliberately not one per action. <c>order_events</c> has eight types of which only
/// two are ever written, which is what a per-action vocabulary produces: it grows a word per action and
/// then nobody uses them. Here the nine <c>FuelVoucher.Status</c> values are already a closed set and a
/// <see cref="VoucherEventTypes.StatusChanged"/> row carries <c>from_status</c>/<c>to_status</c>, so
/// activation, redemption, a block, an expiry and the rest are all the same kind of thing and say which
/// one they are in the columns rather than in the name.
/// </remarks>
public static class VoucherEventTypes
{
    /// <summary>Paper arrived, as a new <c>fuel_vouchers</c> row.</summary>
    public const string Imported = "Imported";

    /// <summary>
    /// <c>status</c> went from one value to another. Carries both, and they must differ.
    /// </summary>
    public const string StatusChanged = "StatusChanged";

    /// <summary>
    /// A paid renewal pushed <c>customer_expiration_date</c> out. Carries the pair.
    /// </summary>
    /// <remarks>
    /// This is where an extension lives. It moves no paper, which is exactly why it is not a
    /// <c>voucher_movements</c> row — and why, the first time it was drafted as one, the ledger's own
    /// CHECK would have had to forbid it.
    /// </remarks>
    public const string Extended = "Extended";

    /// <summary>The soft-delete flag went to true.</summary>
    /// <remarks>
    /// Not a status change: <c>is_deleted</c> is a separate column from <c>status</c>, and a ticket can
    /// be archived while sitting in any state.
    /// </remarks>
    public const string Archived = "Archived";

    /// <summary>The soft-delete flag went back to false.</summary>
    public const string Unarchived = "Unarchived";

    public static readonly IReadOnlyList<string> All =
        [Imported, StatusChanged, Extended, Archived, Unarchived];
}
