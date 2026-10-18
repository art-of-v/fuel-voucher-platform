namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One price an offer was sold at, from the moment it took effect until the next row for the same
/// offer. Append-only: a price is never edited or deleted, only superseded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this table exists.</b> Until now a price lived in exactly one place - the columns of
/// <see cref="FuelOffer"/> - and was overwritten in place. <c>price_updated_at</c> recorded when it
/// last moved and <c>price_updated_by_user_id</c> who moved it, so the question "what did this fuel
/// cost last Tuesday" had no answer: there was one row, holding today's number. Two write paths did
/// not even stamp those two columns, so for a fuel repriced through the operator screen the timestamp
/// was the offer's creation time and the actor was null.
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
/// <b>It is history, not the price.</b> <see cref="FuelOffer"/> keeps its own price columns and they
/// stay the current price: the storefront, checkout and availability all read them, and rewriting
/// those reads to resolve "the latest row" would put a per-row sort in the hot path of every
/// package query for no gain. The invariant this table has to keep is therefore narrow and checkable:
/// <b>the newest row for an offer equals the offer's own price columns</b>. It is maintained from a
/// single choke point - <c>OfferPriceRecorder</c> - which every write path calls after mutating the
/// offer, because a second place that remembered to append a row is a second place that can forget.
/// <c>OfferPriceHistoryIntegrationTests</c> asserts the invariant against the database rather than
/// trusting it.
/// </para>
///
/// <para>
/// <b>The offer is the foreign key, and it RESTRICTs.</b> Matching every other relation in this
/// schema: a history row is a record of something that was sold, so the offer it was sold under must
/// stay readable. Removing the rehearsal pool therefore has to remove its history first - see
/// <c>scripts/remove-qa-catalogue.sql</c>.
/// </para>
///
/// <para>
/// <b>What "current" means</b>, stated once: the row with the greatest
/// <see cref="EffectiveFromUtc"/>, and where two rows share a timestamp - two reprices inside one
/// transaction - the greater <see cref="Id"/>. <see cref="Id"/> is a version 7 UUID, whose leading
/// bits are its creation time, so that tie-break is chronological rather than arbitrary. There is
/// deliberately no unique index on (offer, effective_from): refusing a second reprice in the same
/// millisecond would trade a cosmetic ambiguity for a write that can fail.
/// </para>
///
/// <para>
/// <b>The seeded catalogue is the one writer outside the choke point.</b> <c>SeedData</c> reaches
/// <see cref="FuelOffer"/> through <c>HasData</c>, so EF emits the change as a migration's
/// <c>UpdateData</c> rather than through the application. The initial row for every existing offer
/// was written by the migration that created this table; a later migration that reprices a seeded
/// offer has to append a history row in the same migration, or the seeded fuel's history starts at
/// that migration instead of at the seed.
/// </para>
/// </remarks>
public sealed class FuelOfferPrice
{
    /// <summary>Key. Version 7 UUID, so the id alone orders the rows of one offer chronologically.</summary>
    public Guid Id { get; set; }

    /// <summary>The offer this price belongs to. Natural key, see <see cref="FuelOffer.Id"/>.</summary>
    public string OfferId { get; set; } = null!;

    /// <summary>The network, mirrored from the offer so a price row is readable without the join.</summary>
    public Guid BrandId { get; set; }

    /// <summary>Mirrors the offer's fuel type, for the same reason.</summary>
    public string FuelTypeId { get; set; } = null!;

    // The six per-litre facts, exactly as they stood on the offer at this instant. Copied rather than
    // referenced: the point of a history row is that it cannot change afterwards.
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
    public decimal? PumpPricePerLiter { get; set; }
    public decimal? MinDiscountPerLiter { get; set; }

    /// <summary>Which pool the priced offer belongs to: <see cref="DataKinds.Real"/> or <see cref="DataKinds.Qa"/>.</summary>
    public string DataKind { get; set; } = DataKinds.Real;

    /// <summary>
    /// When this price took effect on the offer. The column a "what did it cost then" query reads,
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
    /// Why the price moved, from <see cref="OfferPriceReasons"/>. The audit trail says what changed;
    /// this says which door it came through.
    /// </summary>
    public string Reason { get; set; } = OfferPriceReasons.Operator;

    /// <summary>The offer. <see cref="FuelOffer.Prices"/> is its side of the relation.</summary>
    public FuelOffer Offer { get; set; } = null!;
}