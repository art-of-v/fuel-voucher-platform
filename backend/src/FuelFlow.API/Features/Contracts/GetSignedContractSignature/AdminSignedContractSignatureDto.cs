namespace FuelFlow.Features.Contracts.GetSignedContractSignature;

/// <summary>
/// One customer's handwritten signature, as the customer drew it.
/// </summary>
/// <remarks>
/// <para>
/// The image is here and NOT in the list. A signature cannot be revoked and cannot be rotated,
/// so it is a capability of its own (<c>AuthPolicies.SignaturePayload</c>): the list carries the
/// row, and this carries the image - and opening it writes an audit row naming the operator
/// (planning #622, S-13).
/// </para>
/// </remarks>
public sealed class AdminSignedContractSignatureDto
{
    /// <summary>The signature's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The contract the signature is on.</summary>
    public Guid ContractId { get; init; }

    /// <summary>The provider the contract is with, when the contract still resolves.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>The provider's name.</summary>
    public string? BrandName { get; init; }

    /// <summary>The company that holds the contract.</summary>
    public Guid? CompanyId { get; init; }

    /// <summary>The company's name.</summary>
    public string? CompanyName { get; init; }

    /// <summary>The user who signed.</summary>
    public Guid UserId { get; init; }

    /// <summary>The customer's name, or their phone number when they gave no name.</summary>
    public string? CustomerName { get; init; }

    /// <summary>When the signature was made.</summary>
    public DateTime SignedAtUtc { get; init; }

    /// <summary>When the contract stops being valid.</summary>
    public DateTime? ExpiresAtUtc { get; init; }

    /// <summary>Whether the term has run out.</summary>
    public bool Expired { get; init; }

    /// <summary>The version of the provider's text the customer actually signed.</summary>
    public short TermsVersion { get; init; }

    /// <summary>The signature image, as the customer drew it.</summary>
    public string? SignatureData { get; init; }
}
