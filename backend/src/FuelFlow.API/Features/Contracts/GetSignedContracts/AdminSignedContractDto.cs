namespace FuelFlow.Features.Contracts.GetSignedContracts;

/// <summary>
/// One signature, as the admin screen reads it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A projection, not the entity.</b> The endpoint answered with the EF entity graph, and the
/// admin client read three fields that were never there - <c>userName</c>, <c>companyName</c>
/// and <c>contractTitle</c> - so the signed list showed empty cells for every row. What a
/// signature is: whose it is, which contract it is on, when, and against which version of the
/// terms.
/// </para>
/// <para>
/// <b>The version that was signed</b> is not the version the contract points at now: a new
/// version of the terms does not rewrite what the customer read, and the audit trail asks which
/// one it was.
/// </para>
/// <para>
/// <b>The image is not here.</b> A customer's handwritten signature cannot be revoked and cannot
/// be rotated, so it is a capability of its own (<c>AuthPolicies.SignaturePayload</c>): the list
/// carries the row, and the image is served per record, with an audit row naming whoever opened
/// it (planning #622, S-13).
/// </para>
/// </remarks>
public sealed class AdminSignedContractDto
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
}
