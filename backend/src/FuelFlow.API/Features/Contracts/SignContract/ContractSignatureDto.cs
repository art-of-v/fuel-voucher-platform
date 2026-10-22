namespace FuelFlow.Features.Contracts.SignContract;

/// <summary>
/// A signature, as the mobile reads it back.
/// </summary>
/// <remarks>
/// The property names ARE the contract: the mobile reads <c>signedAt</c>, so the property is
/// named after the JSON and not after the entity's own <c>SignedAtUtc</c>.
/// </remarks>
public sealed class ContractSignatureDto
{
    /// <summary>The signature's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The contract that was signed.</summary>
    public Guid ContractId { get; init; }

    /// <summary>When the contract was signed.</summary>
    public DateTime SignedAt { get; init; }
}
