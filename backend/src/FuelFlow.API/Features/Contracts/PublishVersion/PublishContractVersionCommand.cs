namespace FuelFlow.Features.Contracts.PublishVersion;

/// <summary>
/// A new version of a provider's text.
/// </summary>
/// <param name="BrandId">The provider whose text this is.</param>
/// <param name="Body">The text the customer reads before signing.</param>
/// <param name="TermDays">How long a contract made against this version is valid, in days.</param>
public sealed record PublishContractVersionCommand(Guid BrandId, string? Body, int TermDays);

/// <summary>
/// The published version, and what the publish did to the contracts that point at it.
/// </summary>
/// <param name="Id">The row's own key.</param>
/// <param name="BrandId">The provider whose text this is.</param>
/// <param name="Version">The version number, counting up from 1 within the provider.</param>
/// <param name="TermDays">How long a contract made against this version is valid, in days.</param>
/// <param name="CreatedAtUtc">When this version was published.</param>
/// <param name="Characters">How much text the version carries, so the operator can see that an upload was read and not stored as an empty page.</param>
/// <param name="ContractsCreated">How many companies were given a contract for this provider by this publish.</param>
public sealed record PublishedContractVersion(
    Guid Id,
    Guid BrandId,
    short Version,
    int TermDays,
    DateTime CreatedAtUtc,
    int Characters,
    int ContractsCreated);
