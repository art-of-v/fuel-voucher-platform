namespace FuelFlow.Features.Brands.AdminUpdateBrand;

/// <summary>
/// The operator's edit of a network.
/// </summary>
/// <param name="Id">The network to edit.</param>
/// <param name="Brand">The brand as the form filled it in. The slug in it is ignored - the slug is immutable over HTTP.</param>
/// <param name="UserId">Who asked for it, for the audit trail.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminUpdateBrandCommand(Guid Id, BrandDto Brand, Guid UserId, string? UserName);

/// <summary>
/// What the edit answered: whether the network was there to be edited.
/// </summary>
/// <param name="Found">False when no live network carries that id - a 404.</param>
public sealed record AdminUpdateBrandResult(bool Found);
