namespace FuelFlow.Features.Brands.AdminDeleteBrand;

/// <summary>
/// The operator's request to archive a network.
/// </summary>
/// <param name="Id">The network to archive.</param>
/// <param name="UserId">Who asked for it, for the audit trail.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminDeleteBrandCommand(Guid Id, Guid UserId, string? UserName);

/// <summary>
/// What the archive answered: whether the network was there to be archived.
/// </summary>
/// <param name="Found">False when no live network carries that id - a 404.</param>
public sealed record AdminDeleteBrandResult(bool Found);
