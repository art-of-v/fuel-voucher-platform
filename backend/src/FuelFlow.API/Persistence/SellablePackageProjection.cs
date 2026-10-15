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
/// The field list is therefore written twice, which is a drift risk, so
/// <c>SellablePackageProjectionTests</c> asserts that this projection and <c>From</c> agree field for
/// field on the same rows. If someone adds a property to one and not the other, that test fails.
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
            PackagePrice = v.PackagePrice,
            PumpPricePerLiter = v.Offer.PumpPricePerLiter,
            MinDiscountPerLiter = v.Offer.MinDiscountPerLiter,
            SupplierPricePerLiter = v.Offer.SupplierPricePerLiter,
            MarginUahPerLiter = v.Offer.MarginUahPerLiter,
            MarginPercent = v.Offer.MarginPercent,
            FinalPricePerLiter = v.Offer.FinalPricePerLiter,
            DataKind = v.Offer.DataKind,
        });
}