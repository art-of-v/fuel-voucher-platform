namespace FuelFlow.Features.Contracts.GetCustomerSignedContracts;

/// <summary>
/// A signature on a contract, as the mobile's signed-contracts list reads it.
/// </summary>
/// <remarks>
/// The property names ARE the contract: the mobile reads <c>signedAt</c> and <c>expiresAt</c>, so
/// they are named after the JSON and not after the entity's own <c>SignedAtUtc</c> and
/// <c>ExpiresAtUtc</c>.
/// </remarks>
public sealed class CustomerSignatureDto
{
    /// <summary>The signature's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The contract that was signed.</summary>
    public Guid ContractId { get; init; }

    /// <summary>The name of the company the contract is for.</summary>
    public string? LegalEntityName { get; init; }

    /// <summary>The provider the contract is with.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>The provider's name.</summary>
    public string? BrandName { get; init; }

    /// <summary>When the contract was signed.</summary>
    public DateTime SignedAt { get; init; }

    /// <summary>When the contract stops being valid.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Whether the term has run out.</summary>
    public bool Expired { get; init; }
}
