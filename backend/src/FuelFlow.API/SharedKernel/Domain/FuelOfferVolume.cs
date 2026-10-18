namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// One volume an offer is sold in — 2 L, 3 L, 5 L — and, optionally, a price that is this nominal's
/// own rather than the offer's rate applied to it.
/// </summary>
/// <remarks>
/// <para>
/// This is the row that <c>fuel_packages</c> rows become. It keeps exactly two facts of its own: how
/// many litres, and whether an operator has deliberately priced this nominal differently.
/// </para>
///
/// <para>
/// <b>What replaced <c>package_price</c>, and why it is nullable.</b> The old column held what the
/// customer pays for this package, stored as <c>round(final_price_per_liter × liters, 2)</c> on every one
/// of the 122 live production volumes — a second copy of a number the offer's rate already determined.
/// Two admin endpoints could write any value into it without checking it against the knobs, and five
/// other write paths then overwrote it back to the arithmetic, so it was at once a duplicate and a
/// "soft" override pretending to be a frozen price: one reprice erased it, and
/// <c>deploy/preflight-db/010</c> existed to notice when it had not.
///
/// <para>
/// <b>An override is not a way around the below-cost guard.</b> It is a price like any other and is
/// refused the same way when it would sell under cost — see
/// <see cref="FuelPricing.IsBelowCost(decimal?,decimal,decimal)"/>, which is asked about the price that
/// will actually be charged.
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
