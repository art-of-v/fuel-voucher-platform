namespace FuelFlow.Features.Contracts.UpdateLegalEntity;

/// <summary>
/// Updates a SPECIFIC legal entity the caller owns.
/// </summary>
/// <param name="UserId">
/// The caller, as the controller read it from the token, or <c>null</c> when there was none. The
/// gate runs before this is looked at, so a refused body answers 400 even when the caller is
/// not authenticated - the order the endpoint has always answered in.
/// </param>
/// <param name="Id">The company to update.</param>
/// <param name="Input">The details to write.</param>
public sealed record UpdateLegalEntityCommand(Guid? UserId, Guid Id, LegalEntityDetails Input);
