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
    /// <summary>
    /// The supplier's registry code - ЄДРОПУ for a company, ІПН for an individual.
    /// <para>
    /// <b>Optional</b> (the business's call, 2026-10-20): a supplier can be recorded before
    /// anyone has looked up their code, and requiring one would mean a supplier who cannot
    /// be invoiced cannot be bought from either.
    /// </para>
    /// <para>
    /// <b>Unique when it is there.</b> Two rows with the same code are the same counterparty
    /// recorded twice, and a business that pays "two" suppliers who are one person pays twice
    /// and reconciles the difference by hand. A code that is not there cannot collide, so the
    /// uniqueness costs nothing for a supplier who has not been identified yet.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Which supply route this supplier sells on: <see cref="AcquisitionSources.Direct"/> or
    /// <see cref="AcquisitionSources.Partner"/>. The source of truth for
    /// <c>FuelVoucher.AcquisitionSource</c>, which the import copies from here rather than asking the
    /// operator to state the same fact twice on the same upload.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Immutable, like the voucher's, and for the same reason from the opposite direction. A supplier
    /// that moves between routes is a different supply relationship, and a voucher bought last quarter
    /// has to keep reporting in the bucket it was bought into - which is precisely why the voucher
    /// column is a copy rather than a join. An editable value here would silently drag historical
    /// vouchers into another bucket the day somebody corrected it.
    /// </para>
    /// <para>
    /// Defaults to <see cref="AcquisitionSources.Direct"/> because every supplier in the database
    /// today sells on that route, so the back-fill is a statement of fact rather than an assumption.
    /// </para>
    /// </remarks>
    public string AcquisitionSource { get; set; } = AcquisitionSources.Default;
}