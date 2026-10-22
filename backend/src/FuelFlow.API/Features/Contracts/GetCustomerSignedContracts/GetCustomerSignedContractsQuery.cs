namespace FuelFlow.Features.Contracts.GetCustomerSignedContracts;

/// <summary>
/// The signatures the caller has made.
/// </summary>
/// <param name="UserId">The caller.</param>
public sealed record GetCustomerSignedContractsQuery(Guid UserId);
