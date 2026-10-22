namespace FuelFlow.Features.Contracts.CreateLegalEntity;

/// <summary>
/// Creates a NEW legal entity for the caller.
/// </summary>
/// <param name="UserId">
/// The caller, as the controller read it from the token, or <c>null</c> when there was none. The
/// gate runs before this is looked at, so a refused body answers 400 even when the caller is
/// not authenticated - the order the endpoint has always answered in.
/// </param>
/// <param name="Input">The details to write.</param>
public sealed record CreateLegalEntityCommand(Guid? UserId, LegalEntityDetails Input);
