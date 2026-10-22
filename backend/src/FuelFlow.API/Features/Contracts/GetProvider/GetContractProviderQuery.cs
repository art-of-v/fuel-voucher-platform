namespace FuelFlow.Features.Contracts.GetProvider;

/// <summary>
/// The provider a piece of contract text belongs to.
/// </summary>
/// <param name="BrandId">The provider's key.</param>
public sealed record GetContractProviderQuery(Guid BrandId);
