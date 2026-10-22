namespace FuelFlow.Features.Contracts.GetMyLegalEntities;

/// <summary>
/// Every legal entity the caller owns.
/// </summary>
/// <param name="UserId">The caller.</param>
public sealed record GetMyLegalEntitiesQuery(Guid UserId);
