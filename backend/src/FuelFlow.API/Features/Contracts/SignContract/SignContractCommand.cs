namespace FuelFlow.Features.Contracts.SignContract;

/// <summary>
/// Records the customer's signature on ONE contract.
/// </summary>
/// <param name="UserId">
/// The caller, as the controller read it from the token, or <c>null</c> when there was none. The
/// gate runs before this is looked at, so a body without a contract answers 400 even when the
/// caller is not authenticated - the order the endpoint has always answered in.
/// </param>
/// <param name="ContractId">The contract being signed.</param>
/// <param name="SignatureData">The signature itself.</param>
public sealed record SignContractCommand(Guid? UserId, Guid ContractId, string? SignatureData);
