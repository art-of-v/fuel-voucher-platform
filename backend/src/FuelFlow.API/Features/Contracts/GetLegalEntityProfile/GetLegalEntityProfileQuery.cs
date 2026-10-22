namespace FuelFlow.Features.Contracts.GetLegalEntityProfile;

/// <summary>
/// The caller's own legal entity - the one the legacy single-company profile is.
/// </summary>
/// <param name="UserId">The caller.</param>
public sealed record GetLegalEntityProfileQuery(Guid UserId);
