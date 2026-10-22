using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers.Suppliers;

public sealed record SupplierDto(
    Guid Id,
    string Name,
    string AcquisitionSource,
    string? LegalForm,
    string? Phone,
    string? Email,
    string? EdrIpn,
    string? Rnkrr,
    string? Address,
    string? Notes,
    bool IsActive)
{
    /// <summary>
    /// The row as the admin screen reads it. The list query builds the same shape in SQL, because
    /// there the projection has to stay translatable.
    /// </summary>
    public static SupplierDto From(Supplier s) => new(
        s.Id, s.Name, s.AcquisitionSource, s.LegalForm, s.Phone, s.Email, s.EdrIpn, s.Rnkrr, s.Address, s.Notes, s.IsActive);
}

public class CreateSupplierRequest
{
    public string? Name { get; set; }

    /// <summary>
    /// Which supply route this supplier sells on - <c>direct</c> or <c>partner</c>. Settable only at
    /// creation: it is immutable afterwards, because every voucher imported from this supplier carries a
    /// copy of it, and an editable value would let a correction made today retroactively disagree with
    /// vouchers bought months ago.
    /// <para>
    /// Absent means <c>direct</c>, which is what every supplier in the database already is. That default
    /// is not a shrug - it is the reason this field can be optional without leaving the second route
    /// unreachable, and the reason it has to be stated deliberately to use it.
    /// </para>
    /// </summary>
    public string? AcquisitionSource { get; set; }

    public string? LegalForm { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    /// <summary>
    /// The supplier's registry code - ЄДРОПУ for a company, ІПН for an individual. Mandatory:
    /// it is what a VAT invoice is issued against, so a supplier without one cannot be invoiced
    /// and the business carries the cost of that.
    /// <para>
    /// Nullable in the request type only so that a missing field is a clean 400 with a sentence
    /// rather than a model-binding error; the validation refuses a blank, and the database refuses
    /// it too.
    /// </para>
    /// </summary>
    public string? EdrIpn { get; set; }
    public string? Rnkrr { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpdateSupplierRequest : CreateSupplierRequest
{
    public bool? IsActive { get; set; }
}
