namespace FuelFlow.Features.Contracts.GetVersions;

/// <summary>
/// The versions of one provider's text.
/// </summary>
/// <param name="BrandId">The provider whose text this is.</param>
public sealed record GetContractVersionsQuery(Guid BrandId);

/// <summary>
/// One version of a provider's text, as the screen reads it.
/// </summary>
/// <remarks>
/// The row itself, and not the entity: the entity carries the provider navigation, which the
/// screen has no use for and the response does not need.
/// </remarks>
/// <param name="Id">The row's own key.</param>
/// <param name="BrandId">The provider whose text this is.</param>
/// <param name="Version">The version number, counting up from 1 within the provider.</param>
/// <param name="Body">The text the customer reads before signing.</param>
/// <param name="TermDays">How long a contract made against this version is valid, in days.</param>
/// <param name="CreatedAtUtc">When this version was published.</param>
/// <param name="CreatedByUserId">Who published it. Null for a version that was seeded.</param>
public sealed record ContractVersionDto(
    Guid Id,
    Guid BrandId,
    short Version,
    string Body,
    int TermDays,
    DateTime CreatedAtUtc,
    Guid? CreatedByUserId);
