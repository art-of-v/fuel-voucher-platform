namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One price a fuel was sold at, from the moment it took effect until the next row for the same
/// fuel. Append-only: a price is never edited or deleted, only superseded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists.</b> Until it was added a price lived in exactly one place - the columns of
/// <c>fuel_offers</c> - and was overwritten in place. <c>price_updated_at</c> recorded when it last
/// moved and <c>price_updated_by_user_id</c> who moved it, so the question "what did this fuel cost
/// last Tuesday" had no answer: there was one row, holding today's number. Two write paths did not
/// even stamp those two columns, so for a fuel repriced through the operator screen the timestamp was
/// the creation time and the actor was null.
/// </para>
///
/// <para>
/// <b>What it is for.</b> Money that already moved cannot be re-derived from the present. The
/// dashboard, reconciliation and the report all multiply today's <c>margin_uah_per_liter</c> by
/// historical order lines, so a margin change today silently restates yesterday's profit - see
/// <c>GetDashboardQueryHandler</c>, <c>GetReconciliationQueryHandler</c> and
/// <c>GetReportQueryHandler</c>. This table is what lets those figures, and any future
/// "price at the moment of sale", be answered from a row that was written when the price was true.
/// </para>
///
/// <para>
/// <b>It is history, not the price.</b> <see cref="FuelTypeEntity"/> keeps its own price columns and
/// they stay the current price: the storefront, checkout and availability all read them, and
/// rewriting those reads to resolve "the latest row" would put a per-row sort in the hot path of
/// every package query for no gain. The invariant this table has to keep is therefore narrow and
/// checkable: <b>the newest row for a fuel equals the fuel's own price columns</b>. It is maintained
/// from a single choke point - <c>PriceHistoryRecorder</c> - which every write path calls after
/// mutating the fuel, because a second place that remembered to append a row is a second place that
/// can forget. <c>ProvidersControllerTests</c> and <c>GetFuelPriceAtIntegrationTests</c> assert it
/// against the database rather than trusting it.
/// </para>
///
/// <para>
/// <b>Keyed by fuel, RESTRICTing to nothing.</b> Until 2026-10-19 this keyed on <c>offer_id</c> and
/// RESTRICTed to the offer, because a price was a record of something sold under an offer and the
/// offer had to stay readable. The offer is gone, so the column is too: the row already carried
/// <see cref="FuelTypeId"/>, so the offer id was a second way of saying which fuel this is. There is
/// no pool either - one price per grade, because nothing can price one grade two ways any more, and
/// <see cref="DataKind"/> now records only where the row was written from.
/// </para>
///
/// <para>
/// <b>What "current" means</b>, stated once: the row with the greatest
/// <see cref="EffectiveFromUtc"/>, and where two rows share a timestamp - two reprices inside one
/// transaction - the greater <see cref="Id"/>. <see cref="Id"/> is a version 7 UUID, whose leading
/// bits are its creation time, so that tie-break is chronological rather than arbitrary. There is
/// deliberately no unique index on (fuel, effective_from): refusing a second reprice in the same
/// millisecond would trade a cosmetic ambiguity for a write that can fail.
/// </para>
/// </remarks>
public sealed class FuelPriceHistory
{
    /// <summary>Key. Version 7 UUID, so the id alone orders the rows of one fuel chronologically.</summary>
    public Guid Id { get; set; }

/// <summary>The network, mirrored from the fuel so a price row is readable without the join.</summary>
    public Guid BrandId { get; set; }

    /// <summary>
    /// The fuel this price belongs to - the key the row was always about. Moved here from
    /// <c>offer_id</c> on 2026-10-19 with the rest of the pricing.
    /// </summary>
    public string FuelTypeId { get; set; } = null!;

    // The six per-litre facts, exactly as the fuel held them at this instant. Copied rather than
    // referenced: the point of a history row is that it cannot change afterwards.
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
    public decimal? PumpPricePerLiter { get; set; }
    public decimal? MinDiscountPerLiter { get; set; }

    /// <summary>
    /// Which pool the write came from: <see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.
    /// It no longer selects a price - there is one price for both pools - and is kept so the audit
    /// trail can say which side of the shelf an edit was made on.
    /// </summary>
    public string DataKind { get; set; } = DataKinds.Real;

    /// <summary>
    /// When this price took effect on the fuel. The column a "what did it cost then" query reads,
    /// and the one the current price is resolved by.
    /// </summary>
    public DateTime EffectiveFromUtc { get; set; }

    /// <summary>When the row was written. Differs from <see cref="EffectiveFromUtc"/> for a backdated one.</summary>
    public DateTime RecordedAtUtc { get; set; }

    /// <summary>
    /// Who moved the price, as a bare uuid with no foreign key - the same reasoning as
    /// <c>provider_event_outbox.changed_by_user_id</c>: the record of who did something must stay
    /// writable even if the account has since been deleted. Null for an unattended import.
    /// </summary>
    public Guid? ChangedByUserId { get; set; }

    /// <summary>
    /// Why the price moved, from <see cref="PriceHistoryReasons"/>. The audit trail says what changed;
    /// this says which door it came through.
    /// </summary>
    public string Reason { get; set; } = PriceHistoryReasons.Operator;
}