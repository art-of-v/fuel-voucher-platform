using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.AdminCreateBrand;

/// <summary>
/// The operator's request to put a network into the catalogue.
/// </summary>
/// <param name="Brand">
/// The brand as the form filled it in. The slug is trimmed and checked here; the id is the
/// server's to assign and one sent by the caller is ignored.
/// </param>
/// <param name="UserId">Who asked for it, for the audit trail.</param>
/// <param name="UserName">Their name, for the audit trail.</param>
public sealed record AdminCreateBrandCommand(BrandDto Brand, Guid UserId, string? UserName);

/// <summary>
/// What the create answered: the brand that was created, or the reason it was refused.
/// </summary>
/// <param name="Success">Whether the brand was created.</param>
/// <param name="Conflict">
/// True when the refusal is a clash with a row that already exists - a 409 - rather than a request
/// that never had a chance - a 400.
/// </param>
/// <param name="Error">The reason, in the words the caller is shown.</param>
/// <param name="Brand">The brand that was created, when it was.</param>
public sealed record AdminCreateBrandResult
{
    public bool Success { get; init; }
    public bool Conflict { get; init; }
    public string? Error { get; init; }
    public Brand? Brand { get; init; }
}
