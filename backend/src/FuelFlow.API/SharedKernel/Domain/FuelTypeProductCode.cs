namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One QR product code a network's vouchers can carry, and the fuel grade it means at that network.
/// </summary>
/// <remarks>
/// <para>
/// <b>The second crosswalk, and a different shape from <see cref="FuelTypeEntity.BrandFuelCode"/>.</b>
/// The brand fuel code is one per grade ("DP"). A product code is one per <em>paper</em>: diesel at
/// OKKO is 9018 <em>and</em> 9518, A-95 is 9015, 9016, 9515 and 9009 — one grade, several codes, and
/// several codes can name one grade. That cardinality is why this is a table and not a column: a
/// column would hold one of the four codes and silently drop the rest.
/// </para>
/// <para>
/// <b>Why it exists.</b> A voucher's QR code is the one signal in a parsed voucher that is not a
/// name. Resolving a fuel by it made the resolution exact — no category, no string matching — which
/// is what removes the last <c>FirstOrDefault</c> over fuel names on the production import path. When
/// no row claims the code, the classifier falls back to the old category path, so this is additive
/// and a row at a time.
/// </para>
/// <para>
/// <b>Nothing populates it automatically.</b> The codes are observed, not derived: they are what
/// paper in the wild carries. An import that learned "code 9018 means diesel" from one voucher's text
/// would be writing an identity from a guess, which is the same mistake as backfilling
/// <see cref="FuelTypeEntity.BrandFuelCode"/> from a name — and here the guess decides what a paid-for
/// voucher is worth.
/// </para>
/// </remarks>
public sealed class FuelTypeProductCode
{
    /// <summary>
    /// The fuel this code means: <c>fuel_types.id</c>. Deliberately not a foreign key, for the reason
    /// <c>fuel_offers.fuel_type_id</c> is not one — the catalog is keyed by text ids that a re-key
    /// migration would have to chase across every child table, and the archive-not-delete policy means
    /// the parent outlives the row either way.
    /// </summary>
    public string FuelTypeId { get; set; } = null!;

    /// <summary>
    /// The network that publishes the code, mirroring <c>fuel_types.station_id</c>. Carried here
    /// because a product code identifies a grade <em>within</em> a network — OKKO's 9018 and KLO's own
    /// codes share a numbering space we cannot prove they do not — and the uniqueness rule needs the
    /// network in the index to say that.
    /// </summary>
    public string StationId { get; set; } = null!;

    /// <summary>The code as it appears in the voucher's QR: <c>"9018"</c>, <c>"45290"</c>.</summary>
    public string ProductCode { get; set; } = null!;

    /// <summary>
    /// When this code was last seen on a voucher. Null until something observes it. A code nobody has
    /// seen in months is how a supplier dropping a grade announces itself without saying anything.
    /// </summary>
    public DateTime? LastSeenAtUtc { get; set; }
}
