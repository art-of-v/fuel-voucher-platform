using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Vouchers;

public class VoucherImport
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public int PageCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = null!;

    /// <summary>
    /// The supplier the operator declared for this whole delivery. Recorded on the batch as well as on every
    /// voucher because "which supplier does this paper belong to" is a fact about the batch that the batch
    /// list has to show: it is the first half of supplier/batch reconciliation. Nullable for a row written
    /// before the supplier picker existed; every new import sets it.
    /// </summary>
    /// <remarks>
    /// This is an operator declaration, not a provenance proof. The payload carries no supplier signature,
    /// serial or registry entry, so reconciling the batch with the delivery note and invoice is the only
    /// operational check there is.
    /// </remarks>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int DecodeIntegrityFailedCount { get; set; }
    public int DecodeIntegrityWarningCount { get; set; }
}
