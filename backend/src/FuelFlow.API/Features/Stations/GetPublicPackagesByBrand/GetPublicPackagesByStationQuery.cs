namespace FuelFlow.Features.Brands.GetPublicPackagesByBrand;

/// <summary>
/// The public package list for one brand. Every caller sees the same real catalogue.
/// </summary>
public sealed record GetPublicPackagesByBrandQuery(Guid BrandId);
