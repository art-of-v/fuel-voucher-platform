using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Persistence;

/// <summary>
/// Renders a volume and its offer as the <see cref="FuelPackage"/> shape the admin API still speaks.
/// </summary>
/// <remarks>
/// <para>
/// The split is an internal shape change; it is not a breaking API change. These endpoints publish a
/// package to operators and scripts, and callers outside this repo read that JSON. Rebuilding the
/// entity view from the pair keeps the response - id, litres, price, and every per-litre knob -
/// byte-for-byte what it was, so this PR needs no client, no migration of integrations and no
/// coordinated deploy.
/// </para>
///
/// <para>
/// <b>Two fields are no longer readable from a package and that is not a loss.</b>
/// <see cref="FuelPackage.FuelName"/> was a denormalised copy of <c>fuel_types.name</c> that every
/// write path had to keep in step - this is the second write path in this file alone. The name now
/// lives in exactly one place, and an offer knows which fuel it is rather than what the fuel is
/// called. <see cref="FuelPackage.OriginalPrice"/> was never stored authoritatively: the storefront
/// recomputes it from the pump price via <c>FuelPricing.OriginalPackagePrice</c> so that a stale
/// stored value cannot leak a supplier cost to an anonymous caller, and this reports it the same way
/// rather than storing a third copy.
/// </para>
/// </remarks>
internal static class PackageProjection
{
    /// <summary>
/// Rebuild the entity view. Takes the fuel rather than just its name: the margin moved to
    /// <c>fuel_types</c>, and passing the name alone would have left this method reading the fuel twice
    /// over - once for what it is called and once for what it costs.
    /// </summary>
    public static FuelPackage ToPackage(this FuelOfferVolume volume, FuelTypeEntity fuel)
        => new()
        {
            Id = volume.Id,
            BrandId = volume.Offer.BrandId,
            FuelTypeId = volume.Offer.FuelTypeId,
            FuelName = fuel.Name,
            Liters = volume.Liters,
            // Computed, not read: the rate x litres. Zero when the fuel is not priced
            // yet - the admin listing still shows that the nominal exists and is unpriced, where the
            // storefront skips it and checkout refuses it.
            Price = FuelPricing.TryPackagePrice(
                    volume.Offer.SupplierPricePerLiter,
                    fuel.MarginUahPerLiter,
                    volume.Offer.PumpPricePerLiter,
                    volume.Offer.MinDiscountPerLiter,
                    volume.Liters) ?? 0m,
            OriginalPrice = FuelPricing.OriginalPackagePrice(
                volume.Offer.PumpPricePerLiter,
                volume.Liters,
                FuelPricing.TryPackagePrice(
                    volume.Offer.SupplierPricePerLiter,
                    fuel.MarginUahPerLiter,
                    volume.Offer.PumpPricePerLiter,
                    volume.Offer.MinDiscountPerLiter,
                    volume.Liters) ?? 0m),
            SupplierPricePerLiter = volume.Offer.SupplierPricePerLiter,
            MarginUahPerLiter = fuel.MarginUahPerLiter,
            MarginPercent = volume.Offer.MarginPercent,
            // Computed, not read: the admin JSON keeps the field (the panel renders it), and it is derived
            // from the fuel's margin here rather than off `offer.ComputedFinalPricePerLiter`, which still
            // reads the offer's own copy of a number nothing writes any more.
            FinalPricePerLiter = FuelPricing.FinalPerLiterOrNull(
                volume.Offer.SupplierPricePerLiter,
                fuel.MarginUahPerLiter,
                volume.Offer.PumpPricePerLiter,
                volume.Offer.MinDiscountPerLiter) ?? 0m,
            PumpPricePerLiter = volume.Offer.PumpPricePerLiter,
            MinDiscountPerLiter = volume.Offer.MinDiscountPerLiter,
            DataKind = volume.Offer.DataKind,
            CreatedAtUtc = volume.CreatedAtUtc,
            UpdatedAtUtc = volume.UpdatedAtUtc,
            IsDeleted = volume.IsDeleted,
            DeletedAtUtc = volume.DeletedAtUtc,
        };

    /// <summary>
    /// Rebuild the entity view for a list, resolving fuels in one lookup rather than per row.
    /// </summary>
    public static List<FuelPackage> ToPackages(
        this IReadOnlyCollection<FuelOfferVolume> volumes,
        IReadOnlyDictionary<string, FuelTypeEntity> fuels)
        => volumes.Select(v => v.ToPackage(
                fuels.TryGetValue(v.Offer.FuelTypeId, out var fuel) ? fuel : MissingFuel))
                  .ToList();

    /// <summary>
    /// Stands in for a fuel that is not in the lookup, so a missing one renders as an unnamed,
    /// unpriced package instead of throwing halfway through the list. Reaching here means an offer
    /// names a fuel that does not exist, which the FK added in 20261018104336 now refuses at write
    /// time - the fallback is for a lookup that was simply not asked for the whole table.
    /// </summary>
    private static readonly FuelTypeEntity MissingFuel = new()
    {
        Id = string.Empty,
        Name = string.Empty,
        BrandId = string.Empty
    };
}