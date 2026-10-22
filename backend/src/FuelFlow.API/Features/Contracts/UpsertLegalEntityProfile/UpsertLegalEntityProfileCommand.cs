namespace FuelFlow.Features.Contracts.UpsertLegalEntityProfile;

/// <summary>
/// Writes the caller's own legal entity - the legacy single-company profile path, which upserts
/// by <c>UserId</c>.
/// </summary>
/// <param name="UserId">
/// The caller, as the controller read it from the token, or <c>null</c> when there was none. The
/// gate runs before this is looked at, so a refused body answers 400 even when the caller is
/// not authenticated - the order the endpoint has always answered in.
/// </param>
/// <param name="Input">The details to write.</param>
public sealed record UpsertLegalEntityProfileCommand(Guid? UserId, LegalEntityDetails Input);
