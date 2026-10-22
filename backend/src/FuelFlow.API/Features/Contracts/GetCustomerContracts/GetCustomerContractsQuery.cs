namespace FuelFlow.Features.Contracts.GetCustomerContracts;

/// <summary>
/// The contracts the caller is offered to sign.
/// </summary>
/// <param name="UserId">The caller.</param>
public sealed record GetCustomerContractsQuery(Guid UserId);
