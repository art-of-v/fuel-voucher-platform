namespace FuelFlow.Features.Brands.GetPublicFuelTypes;

/// <summary>
/// The public fuel list. The caller's id decides whose stock counts as available.
/// </summary>
public sealed record GetPublicFuelTypesQuery(Guid? CallerUserId);
