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
    /// Rebuild the entity view. Takes the fuel's name because it is not on the offer: station and fuel
    /// are natural keys in this schema, so the offer carries no navigation to load it from.
    /// </summary>
    public static FuelPackage ToPackage(this FuelOfferVolume volume, string fuelName)
        => new()
        {
            Id = volume.Id,
            StationId = volume.Offer.StationId,
            FuelTypeId = volume.Offer.FuelTypeId,
            FuelName = fuelName,
            Liters = volume.Liters,
            Price = volume.PackagePrice,
            OriginalPrice = FuelPricing.OriginalPackagePrice(
                volume.Offer.PumpPricePerLiter, volume.Liters, volume.PackagePrice),
            SupplierPricePerLiter = volume.Offer.SupplierPricePerLiter,
            MarginUahPerLiter = volume.Offer.MarginUahPerLiter,
            MarginPercent = volume.Offer.MarginPercent,
            // Computed, not read: the admin JSON keeps the field (the panel renders it), but the offer no
            // longer stores it.
            FinalPricePerLiter = volume.Offer.ComputedFinalPricePerLiter,
            PumpPricePerLiter = volume.Offer.PumpPricePerLiter,
            MinDiscountPerLiter = volume.Offer.MinDiscountPerLiter,
            DataKind = volume.Offer.DataKind,
            CreatedAtUtc = volume.CreatedAtUtc,
            UpdatedAtUtc = volume.UpdatedAtUtc,
            IsDeleted = volume.IsDeleted,
            DeletedAtUtc = volume.DeletedAtUtc,
        };

    /// <summary>
    /// Rebuild the entity view for a list, resolving fuel names in one lookup rather than per row.
    /// </summary>
    public static List<FuelPackage> ToPackages(
        this IReadOnlyCollection<FuelOfferVolume> volumes,
        IReadOnlyDictionary<string, string> fuelNames)
        => volumes.Select(v => v.ToPackage(fuelNames.TryGetValue(v.Offer.FuelTypeId, out var name) ? name : string.Empty))
                  .ToList();
}