using FuelFlow.Features.Contracts.SharedModels;

namespace FuelFlow.Features.Contracts;

/// <summary>
/// A legal entity, as the client reads it.
/// </summary>
/// <remarks>
/// The projection the legal-entity endpoints answer with. It used to be an anonymous object
/// built by a private helper in the controller, which every endpoint had to remember to call;
/// a named type keeps the shape in one place and lets the handlers return it.
/// <para>
/// The property names ARE the contract: the mobile reads <c>createdAt</c> and <c>updatedAt</c>,
/// so the properties are named after the JSON and not after the entity's own
/// <c>CreatedAtUtc</c> / <c>UpdatedAtUtc</c>.
/// </para>
/// </remarks>
public sealed class LegalEntityDto
{
    /// <summary>The company's own key.</summary>
    public Guid Id { get; init; }

    /// <summary>The company's name.</summary>
    public string Name { get; init; } = null!;

    /// <summary>The company's registry code (EDRPOU).</summary>
    public string Edrpou { get; init; } = null!;

    /// <summary>The company's VAT number, when it has one.</summary>
    public string? VatNumber { get; init; }

    /// <summary>The company's address.</summary>
    public string? Address { get; init; }

    /// <summary>The name of the company's director.</summary>
    public string? DirectorName { get; init; }

    /// <summary>The company's phone number.</summary>
    public string? Phone { get; init; }

    /// <summary>The company's email address.</summary>
    public string? Email { get; init; }

    /// <summary>When the company was registered.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the company's details were last changed.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// The projection of one company.
    /// </summary>
    /// <param name="entity">The company to project.</param>
    public static LegalEntityDto From(LegalEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Edrpou = entity.Edrpou,
        VatNumber = entity.VatNumber,
        Address = entity.Address,
        DirectorName = entity.DirectorName,
        Phone = entity.Phone,
        Email = entity.Email,
        CreatedAt = entity.CreatedAtUtc,
        UpdatedAt = entity.UpdatedAtUtc,
    };
}
