namespace FuelFlow.Features.Contracts.GetAdminContracts;

/// <summary>
/// One contract, as the admin screen reads it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A projection, not the entity.</b> This endpoint answered with the EF entity graph, which
/// carried the customer's phone number, the company's EDRPOU and address, and every column of
/// the contract - and the admin client then read five fields the server had never returned, one
/// of which (<c>status</c>) it called <c>.toLowerCase()</c> on, so the tab threw on the first
/// real row. A DTO is what the screen actually shows, and it is the item left open in the
/// security audit ("Project DTOs instead of EF entities across the rest of the API").
/// </para>
/// <para>
/// <b>What the screen needs to know.</b> Which provider and which company the contract is for,
/// who the customer is, when it was made, how long it runs, which version of the provider's text
/// it is against, and whether it has been signed.
/// </para>
/// </remarks>
public sealed class AdminContractDto
{
    /// <summary>The contract's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The provider the contract is with.</summary>
    public Guid BrandId { get; init; }

    /// <summary>The provider's name - the label the screen shows.</summary>
    public string? BrandName { get; init; }

    /// <summary>The company that holds the contract.</summary>
    public Guid LegalEntityId { get; init; }

    /// <summary>The company's name.</summary>
    public string? CompanyName { get; init; }

    /// <summary>The user who owns the company and signs the contract.</summary>
    public Guid UserId { get; init; }

    /// <summary>The customer's name, or their phone number when they gave no name.</summary>
    public string? CustomerName { get; init; }

    /// <summary>When the contract was made.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>When the contract stops being valid.</summary>
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>Whether the term has run out. The sign endpoint refuses an expired contract.</summary>
    public bool Expired { get; init; }

    /// <summary>The version of the provider's text this contract is against.</summary>
    public short TermsVersion { get; init; }

    /// <summary>Whether the contract has been signed.</summary>
    public bool Signed { get; init; }

    /// <summary>When it was signed, if it was.</summary>
    public DateTime? SignedAtUtc { get; init; }
}
