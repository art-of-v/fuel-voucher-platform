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
/// <param name="nominals">Every size any fuel is sold in, per pool. Filtered to the fuel below.</param>
    /// <param name="brands">
    /// The networks, for the slug beside each fuel's uuid. Joined rather than resolved afterwards:
    /// <see cref="SellablePackage.BrandSlug"/> is what the stock key matches a voucher's brand NAME
    /// against, so a projection that left it empty would produce packages nothing can be found by.
    /// </param>
    public static IQueryable<SellablePackage> Project(
            this IQueryable<FuelTypeEntity> fuels,
            IQueryable<FuelNominal> nominals,
            IQueryable<Brand> brands)
        => from fuel in fuels
           join nominal in nominals on fuel.Id equals nominal.FuelTypeId
           join brand in brands on fuel.BrandId equals brand.Id
           select new SellablePackage
           {
               PackageId = PackageIds.For(fuel.Id, nominal.DataKind, nominal.Liters),
               BrandId = fuel.BrandId,
               BrandSlug = brand.Slug,
               FuelTypeId = fuel.Id,
               Liters = nominal.Liters,
               // Every price figure is the fuel's now. The projection and
               // SellablePackage.From read the same columns, and neither has an offer to disagree
               // with the other.
               PumpPricePerLiter = fuel.PumpPricePerLiter,
               SupplierPricePerLiter = fuel.SupplierPricePerLiter,
               MarginUahPerLiter = fuel.MarginUahPerLiter,
               MarginPercent = fuel.MarginPercent,
               DataKind = nominal.DataKind,
           };
    }
