using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Persistence;

/// <summary>
/// Projects a fuel's nominal sizes joined to the offer that sells them, as <see cref="SellablePackage"/>, in
/// the database.
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
    /// The field list is written twice, which is a drift risk, and the guard named here has never existed -
    /// <c>SellablePackageProjectionTests</c> is referenced by this comment and by nothing else in the
    /// repository. Stated rather than left to look enforced: the two lists are shorter every time a
    /// computed member moves the price out of the projection, and the remaining risk is an <c>init</c>
    /// property that one list forgets.
    /// </para>
    /// </remarks>
    internal static class SellablePackageProjection
    {
        /// <summary>
        /// Server-side projection. The nominal row is the grain: one package per size the operator allows
        /// for sale, for one offer's pool.
        /// </summary>
        /// <param name="offers">
        /// The caller's pool. The offer supplies the price knobs and the identity; it no longer carries
        /// a list of sizes, so it cannot be the grain on its own.
        /// </param>
/// <param name="nominals">Every size any fuel is sold in. Filtered to the offer's fuel below.</param>
    /// <param name="brands">
    /// The networks, for the slug beside each offer's uuid. Joined rather than resolved afterwards:
    /// <see cref="SellablePackage.BrandSlug"/> is what the stock key matches a voucher's brand NAME
    /// against, so a projection that left it empty would produce packages nothing can be found by.
    /// </param>
    public static IQueryable<SellablePackage> Project(
            this IQueryable<FuelOffer> offers,
            IQueryable<FuelNominal> nominals,
            IQueryable<Brand> brands)
        => from offer in offers
           join nominal in nominals on offer.FuelTypeId equals nominal.FuelTypeId
           join brand in brands on offer.BrandId equals brand.Id
           select new SellablePackage
           {
               PackageId = PackageIds.For(offer.Id, nominal.Liters),
               OfferId = offer.Id,
               BrandId = offer.BrandId,
               BrandSlug = brand.Slug,
               FuelTypeId = offer.FuelTypeId,
               Liters = nominal.Liters,
               PumpPricePerLiter = offer.PumpPricePerLiter,
               MinDiscountPerLiter = offer.MinDiscountPerLiter,
               SupplierPricePerLiter = offer.SupplierPricePerLiter,
               // The fuel's margin, not the offer's: a margin is a fact about the grade, so the two pools
               // of one fuel cannot quote two prices. The join is the same server-side projection the rest
               // of this query already is - no extra round trip, no Include.
               MarginUahPerLiter = offer.FuelType!.MarginUahPerLiter,
               MarginPercent = offer.MarginPercent,
               DataKind = offer.DataKind,
           };
    }
