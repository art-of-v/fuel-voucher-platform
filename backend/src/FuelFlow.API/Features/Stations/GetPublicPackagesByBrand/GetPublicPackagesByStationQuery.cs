namespace FuelFlow.Features.Brands.GetPublicPackagesByBrand;

/// <param name="CallerUserId">The signed-in caller, or null — see <see cref="GetPublicPackagesQuery.CallerUserId"/>.</param>
public sealed record GetPublicPackagesByBrandQuery(string BrandId, Guid? CallerUserId);