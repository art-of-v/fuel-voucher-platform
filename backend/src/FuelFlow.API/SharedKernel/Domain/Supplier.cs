namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A supplier — the party we buy fuel vouchers from and hand lapsed ones back to. Deliberately NOT the
/// same thing as a <c>LegalEntity</c>: that is our own company trading through the platform (it belongs
/// to a <c>User</c> and always has an Edrpou), while a supplier is an external counterparty we settle
/// with. They also have different shapes — an ФОП has no Edrpou, an LLC has no ІПН — so the requisite
/// fields are all optional and the legal form is what tells you which apply.
/// </summary>
/// <remarks>
/// Deliberately NOT tied to a brand. A supplier resells whatever brands they carry, so the brand is a
/// property of the vouchers, not of the supplier. Deactivation rather than deletion: past vouchers and
/// exchange rows keep pointing at this row, and that audit trail has to keep naming a real party.
/// </remarks>
public sealed class Supplier
{
    public Guid Id { get; set; }

    /// <summary>Display name — "ФОП Стретович Микола", "ТОВ ОККО-Постальна". Unique among suppliers.</summary>
    public string Name { get; set; } = null!;

    /// <summary>ФОП / ТОВ / ТзОВ / ФОП-ЄЗ / інше. Drives which requisites are meaningful.</summary>
    public string? LegalForm { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>ЄДРОПУ/ІПН — the company's or sole proprietor's registry code. Null for individuals.</summary>
    public string? EdrIpn { get; set; }

    /// <summary>РНОКР — the tax register number. Individuals have one too; companies may omit it.</summary>
    public string? Rnkrr { get; set; }

    public string? Address { get; set; }

    /// <summary>Free-form: settlement terms, who to call, what the invoice must include.</summary>
    public string? Notes { get; set; }

    /// <summary>Inactive suppliers stay on past vouchers for audit but are not offered to the operator.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}