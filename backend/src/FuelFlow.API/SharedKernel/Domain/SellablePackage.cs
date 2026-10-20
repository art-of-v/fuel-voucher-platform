namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A size joined to the fuel that sells it: everything a caller needs to price or sell one line of
/// fuel, with the per-litre facts read once from the fuel rather than repeated per size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> A caller that wants to price a line needs two things: the per-litre
/// facts, and the size the fuel is sold in. The facts live on the fuel; the size is a row of its own
/// (a nominal). Neither is the other, so something has to join them - and the join is this type.
/// </para>
///
/// <para>
/// The alternative was to pass the fuel plus a number into every pricing call. This is the
/// same thing with a name, and the property names are deliberately IDENTICAL to
/// <see cref="FuelPackage"/>'s so that <c>ServerPricing</c> — the money path — changes type rather
/// than logic. Nothing in that file decides anything about where a price comes from; it recomputes
/// from knobs and it should not have to care that the knobs moved to another table.
/// </para>
///
/// <para>
/// <b>PackagePrice is not the same fact as the fuel's price times litres</b>, and is not derived from it
/// at read time: it is what the customer is charged for this package, recomputed the way
/// <c>order_line_items.unit_price</c> is. One price per litre per grade, so a nominal cannot be sold
/// at a number of its own; the relation is asserted in tests and not enforced by a constraint.
/// </para>
/// </remarks>
public sealed record SellablePackage
{
    /// <summary>
    /// The size this package sells in, composed from the fuel and its nominal list. Not a row
    /// id any more: no column, no foreign key, nothing to keep in step - see <see cref="PackageIds"/>.
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

/// <summary>
    /// No offer id here. It went with the offer on 2026-10-19; <see cref="PackageId"/> already says
    /// the fuel and the pool, and nothing downstream was reading it.
    /// </summary>
    public Guid BrandId { get; init; }

    /// <summary>
    /// The network's own code (<c>"okko"</c>). Carried beside <see cref="BrandId"/> so the storefront
    /// can group and label a shelf without a second round trip, and so a log line or a support
    /// ticket can be read by a human.
    /// </summary>
    public string BrandSlug { get; init; } = string.Empty;

    public Guid FuelTypeId { get; init; }

    public decimal Liters { get; init; }

    public decimal? PumpPricePerLiter { get; init; }

    public decimal? MinDiscountPerLiter { get; init; }

    public decimal? SupplierPricePerLiter { get; init; }

    public decimal? MarginUahPerLiter { get; init; }

    public decimal? MarginPercent { get; init; }

    /// <summary>
    /// The customer price per litre these four knobs produce, or <c>null</c> while the fuel has no
    /// cost or no margin. Computed, not carried: <c>fuel_offers</c> stopped storing it on 2026-10-17,
    /// and a member that had to be projected from a column nobody selects is a member two projections
    /// can forget in different ways. Derived here instead, once, for both.
    /// </summary>
    public decimal? ComputedFinalPricePerLiter =>
        FuelPricing.FinalPerLiterOrNull(
            SupplierPricePerLiter, MarginUahPerLiter, PumpPricePerLiter, MinDiscountPerLiter);

    /// <summary>
    /// What this nominal costs, through <see cref="FuelPricing.PackagePrice"/>: the rate × these
    /// litres. One price per litre per grade, so a nominal cannot be sold at a number of its own.
    /// <para>
    /// Throws when the fuel has no cost or no margin, which is the state of a fuel nobody has priced
    /// yet. Callers that can meet it check <see cref="ComputedFinalPricePerLiter"/> first or handle the
    /// absence; this is deliberately loud rather than returning zero, because a zero shelf price is a
    /// free fuel.
    /// </para>
    /// </summary>
    public decimal ComputedPackagePrice =>
        FuelPricing.PackagePrice(
            SupplierPricePerLiter,
            MarginUahPerLiter,
            PumpPricePerLiter,
            MinDiscountPerLiter,
            Liters);

    /// <summary>Which pool this came from. Carried so a caller can assert it matched what it asked for.</summary>
    public string DataKind { get; init; } = string.Empty;

    /// <summary>
    /// Compose the pair. The one place this join is expressed, and the only place identity is set.
    /// <para>
    /// The fuel is passed in rather than reached through a navigation because this runs after
    /// materialisation, where a navigation is only populated if the query asked for it - and the
    /// callers here project server-side, so nothing asked. A margin read off a navigation that happens
    /// to be loaded is a margin that is silently zero everywhere else.
    /// </para>
    /// <para>
    /// The size comes in as a number rather than as a nominal row. By the time a package is being
    /// priced the row has done its job - it said the size exists - and carrying an entity this far
    /// would suggest the caller needs it, which it never does.
    /// </para>
    /// </summary>
    public static SellablePackage From(
FuelTypeEntity fuel,
        string dataKind,
        decimal liters,
        string brandSlug) => new()
    {
        // Identity from the triple the nominal is keyed on, not from a row that no longer exists.
        PackageId = PackageIds.For(fuel.Id, dataKind, liters),
        BrandId = fuel.BrandId,
        BrandSlug = brandSlug,
        FuelTypeId = fuel.Id,
        Liters = liters,
        PumpPricePerLiter = fuel.PumpPricePerLiter,
        MinDiscountPerLiter = fuel.MinDiscountPerLiter,
        SupplierPricePerLiter = fuel.SupplierPricePerLiter,
        MarginUahPerLiter = fuel.MarginUahPerLiter,
        MarginPercent = fuel.MarginPercent,
        DataKind = dataKind,
    };
}
