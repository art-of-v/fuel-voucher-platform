namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One volume an offer is sold in — 2 L, 3 L, 5 L — and the price of that package.
/// </summary>
/// <remarks>
/// <para>
/// This is the row that <c>fuel_packages</c> rows become. It keeps exactly two facts of its own: how
/// many litres, and what the whole package costs. Everything else about pricing moved to
/// <see cref="FuelOffer"/>, because it was true of the offer rather than of the volume.
/// </para>
///
/// <para>
/// <b><see cref="PackagePrice"/> is redundant with the offer price times the volume, and that is
/// deliberate.</b> It is what the customer is charged for this package, and freezing it is the same
/// reasoning as <c>order_line_items.unit_price</c>: a checkout must be reproducible after the price
/// moves, and the offer price at the moment of sale is not recorded anywhere else. The relation
/// <c>PackagePrice = round(FinalPricePerLiter × liters, 2)</c> holds today and is asserted in
/// <c>FuelOfferVolumeConsistencyTests</c>, but it is not a database constraint: the retailer's own
/// price for a 50 L bottle is allowed to differ from the arithmetic.
/// </para>
///
/// <para>
/// <b>The id is the id of the <c>fuel_packages</c> row this replaces</b> — <c>okko-95-10</c>,
/// <c>qa-pkg-okko-95-10</c>. That keeps the migration idempotent and means an id quoted in a log, a
/// support ticket or a saved cart still resolves to the same thing afterwards. It is <em>not</em> a
/// foreign key to anything: nothing in the schema points at a package row
/// (<c>order_line_items</c> carries <c>Brand + FuelTypeId + Liters</c> and no <c>PackageId</c>).
/// </para>
///
/// <para>
/// <b><see cref="Uniqueness"/> is the guarantee this split was for.</b> <c>UNIQUE (offer_id, liters)</c>
/// makes a duplicate volume for one offer impossible at the database level, so the ambiguous
/// checkout lookup that planning #315 had to resolve with an <c>ORDER BY</c> can never come back.
/// </para>
/// </remarks>
public sealed class FuelOfferVolume
{
    /// <summary>Inherited from the <c>fuel_packages</c> row this replaces. See the class remarks.</summary>
    public string Id { get; set; } = null!;

    /// <summary>The offer this volume belongs to. Natural key, see <see cref="FuelOffer.Id"/>.</summary>
    public string OfferId { get; set; } = null!;

    /// <summary>Volume in litres. Bounded above zero by <c>ck_fuel_offer_volumes_liters_positive</c>.</summary>
    public decimal Liters { get; set; }

    /// <summary>
    /// What the whole package costs, in UAH. Bounded at zero by
    /// <c>ck_fuel_offer_volumes_package_price_non_negative</c>.
    /// </summary>
    public decimal PackagePrice { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Archive, not delete. Carried independently of the offer's own flag: an offer keeps its
    /// per-litre price while a single volume leaves the catalogue.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the volume was archived, or null while it is live.</summary>
    public DateTime? DeletedAtUtc { get; set; }

    /// <summary>The offer. <c>RESTRICT</c> on delete, matching every other relation in the schema.</summary>
    public FuelOffer Offer { get; set; } = null!;
}