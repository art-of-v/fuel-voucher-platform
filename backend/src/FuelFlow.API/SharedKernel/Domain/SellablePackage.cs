namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A volume joined to the offer that sells it: everything a caller needs to price or sell one line of
/// fuel, with the per-litre facts read once from <see cref="FuelOffer"/> rather than copied per volume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists.</b> Before <c>fuel_offers</c>, a caller that wanted to price a line read a
/// <see cref="FuelPackage"/> row and every per-litre column on it. With the split, that data lives on
/// the offer and the volume carries only litres and a package price — so a caller needs both rows and
/// something has to join them.
/// </para>
///
/// <para>
/// The alternative was to pass a <c>FuelOfferVolume</c> plus a <c>FuelOffer</c> pair into every pricing
/// call. This is the same thing with a name, and the property names are deliberately IDENTICAL to
/// <see cref="FuelPackage"/>'s so that <c>ServerPricing</c> — the money path — changes type rather
/// than logic. Nothing in that file decides anything about where a price comes from; it recomputes
/// from knobs and it should not have to care that the knobs moved to another table.
/// </para>
///
/// <para>
/// <b>PackagePrice is not the same fact as the offer price times litres</b>, and is not derived from it
/// at read time: it is what the customer is charged for this package, frozen the way
/// <c>order_line_items.unit_price</c> is. The retailer's price for a 50 L bottle is allowed to differ
/// from the arithmetic, so the relation is asserted in tests and not enforced by a constraint.
/// </para>
/// </remarks>
public sealed record SellablePackage
{
    /// <summary>Volume id — the <c>fuel_packages.id</c> the row replaced, so ids still resolve.</summary>
    public string PackageId { get; init; } = string.Empty;

    public string OfferId { get; init; } = string.Empty;

    public string BrandId { get; init; } = string.Empty;

    public string FuelTypeId { get; init; } = string.Empty;

    public decimal Liters { get; init; }

    /// <summary>

    public decimal? PumpPricePerLiter { get; init; }

    public decimal? MinDiscountPerLiter { get; init; }

    public decimal? SupplierPricePerLiter { get; init; }

    public decimal? MarginUahPerLiter { get; init; }

    public decimal? MarginPercent { get; init; }

    /// <summary>
    /// The customer price per litre these four knobs produce, or <c>null</c> while the offer has no
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
    /// Compose the triple. The one place this join is expressed, and the only place identity is set.
    /// <para>
    /// The fuel is passed in rather than reached through <c>offer.FuelType</c> because this runs after
    /// materialisation, where the navigation is only populated if the query asked for it - and the
    /// callers here project server-side, so nothing asked. A margin read off a navigation that happens
    /// to be loaded is a margin that is silently zero everywhere else.
    /// </para>
    /// </summary>
    public static SellablePackage From(
        FuelOffer offer,
        FuelOfferVolume volume,
        FuelTypeEntity fuel) => new()
    {
        PackageId = volume.Id,
        OfferId = offer.Id,
        BrandId = offer.BrandId,
        FuelTypeId = offer.FuelTypeId,
        Liters = volume.Liters,
        PumpPricePerLiter = offer.PumpPricePerLiter,
        MinDiscountPerLiter = offer.MinDiscountPerLiter,
        SupplierPricePerLiter = offer.SupplierPricePerLiter,
        MarginUahPerLiter = fuel.MarginUahPerLiter,
        MarginPercent = offer.MarginPercent,
        DataKind = offer.DataKind,
    };
}
