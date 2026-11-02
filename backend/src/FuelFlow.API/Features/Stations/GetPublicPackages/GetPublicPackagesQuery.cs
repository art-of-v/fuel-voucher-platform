namespace FuelFlow.Features.Brands.GetPublicPackages;

/// <summary>
/// The public package list. Every caller sees the same real catalogue: there is no per-caller
/// stock pool any more, so the query carries no identity.
/// </summary>
public sealed record GetPublicPackagesQuery;
