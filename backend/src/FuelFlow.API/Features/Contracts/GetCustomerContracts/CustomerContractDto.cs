namespace FuelFlow.Features.Contracts.GetCustomerContracts;

/// <summary>
/// One contract, as the mobile's list of documents to sign reads it.
/// </summary>
/// <remarks>
/// The property names ARE the contract: the mobile reads <c>termsVersion</c> and
/// <c>termsText</c>, so they are named after the JSON and not after the entity's own
/// <c>TermsVersion</c>.
/// </remarks>
public sealed class CustomerContractDto
{
    /// <summary>The contract's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The company the contract is for.</summary>
    public Guid LegalEntityId { get; init; }

    /// <summary>The name of the company the contract is for.</summary>
    public string? LegalEntityName { get; init; }

    /// <summary>The provider the contract is with.</summary>
    public Guid BrandId { get; init; }

    /// <summary>The provider's name.</summary>
    public string? BrandName { get; init; }

    /// <summary>When the contract was made.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the contract stops being valid.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Whether the term has run out.</summary>
    public bool Expired { get; init; }

    /// <summary>The version of the provider's text this contract is against.</summary>
    public short TermsVersion { get; init; }

    /// <summary>The text the customer reads before signing, when it has been published.</summary>
    public string? TermsText { get; init; }

    /// <summary>Whether there is a text to read at all.</summary>
    public bool HasText { get; init; }
}
