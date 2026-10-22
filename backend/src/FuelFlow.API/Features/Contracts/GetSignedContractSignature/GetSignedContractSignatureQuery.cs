namespace FuelFlow.Features.Contracts.GetSignedContractSignature;

/// <summary>
/// One customer's handwritten signature, as the customer drew it.
/// </summary>
/// <param name="SignatureId">The signature's own key - the <c>user_contracts</c> row.</param>
/// <param name="ActingUserId">
/// The staff member who asked for the image. It is part of the query rather than read from a
/// service locator, because the audit row this writes must name the actor - and an audit row
/// without an actor is a row nobody can answer for.
/// </param>
/// <param name="ActingUserName">Their name, so the row still reads after a rename.</param>
public sealed record GetSignedContractSignatureQuery(
    Guid SignatureId,
    Guid ActingUserId,
    string? ActingUserName);
