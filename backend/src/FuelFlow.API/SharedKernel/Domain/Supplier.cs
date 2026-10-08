using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// A supplier — the company we actually bought vouchers from. Distinct from the <c>stations</c> brand:
/// OKKO is <i>what</i> a voucher is, a supplier is <i>who</i> issued it, and the same brand is dealt in by
/// several suppliers. Exchanges go back to the supplier the vouchers came from, so the link has to survive
/// on the voucher itself rather than being inferred from the brand.
/// </summary>
public sealed class Supplier
{
    public Guid Id { get; set; }

    /// <summary>Display name, unique among active suppliers so the operator cannot pick between two identical entries.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Free-form contact: phone, email, company registry number — whatever the operator needs to reconcile.</summary>
    public string? ContactInfo { get; set; }

    /// <summary>
    /// Brand this supplier deals in, when known ("okko", "wog", …). Deliberately not unique and not
    /// required: a supplier may deal in several brands, and we may not know the brand before the first
    /// delivery. Never a uniqueness constraint — that would wrongly forbid one supplier per brand.
    /// </summary>
    public string? StationId { get; set; }

    /// <summary>Inactive suppliers stay on past vouchers for audit but are not offered to the operator.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}