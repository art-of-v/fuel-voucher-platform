using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Persistence;

/// <summary>
/// Projects volumes joined to their offer as <see cref="SellablePackage"/>, in the database.
/// </summary>
/// <remarks>
/// <para>
/// This is the queryable twin of <see cref="SellablePackage.From"/>. Both exist because the two are
/// used from different places: EF can project an object initialiser over a joined query, and can
/// materialise entities and then compose them, but not one shape for both without either giving up
/// server-side projection or writing the field list twice.
/// </para>
///
/// <para>
/// The field list is written twice, which is a drift risk, and the guard named here has never existed —
/// <c>SellablePackageProjectionTests</c> is referenced by this comment and by nothing else in the
/// repository. Stated rather than left to look enforced: the two lists are shorter every time a
/// computed member moves the price out of the projection, and the remaining risk is an <c>init</c>
/// property that one list forgets.
/// </para>
/// </remarks>
internal static class SellablePackageProjection
{
    /// <summary>
    /// Server-side projection. The volume row is the grain: one row per sellable package, which is
    /// what every caller before the split was iterating.
    /// </summary>
    public static IQueryable<SellablePackage> Project(this IQueryable<FuelOfferVolume> volumes)
        => volumes.Select(v => new SellablePackage
        {
            PackageId = v.Id,
            OfferId = v.OfferId,
            StationId = v.Offer.StationId,
            FuelTypeId = v.Offer.FuelTypeId,
            Liters = v.Liters,
            PriceOverrideUah = v.PriceOverrideUah,
            PumpPricePerLiter = v.Offer.PumpPricePerLiter,
            MinDiscountPerLiter = v.Offer.MinDiscountPerLiter,
            SupplierPricePerLiter = v.Offer.SupplierPricePerLiter,
            // The fuel's margin, not the offer's: a margin is a fact about the grade, so the two pools
            // of one fuel cannot quote two prices. The join is the same server-side projection the rest
            // of this query already is - no extra round trip, no Include.
            MarginUahPerLiter = v.Offer.FuelType!.MarginUahPerLiter,
            MarginPercent = v.Offer.MarginPercent,
            DataKind = v.Offer.DataKind,
        });
}