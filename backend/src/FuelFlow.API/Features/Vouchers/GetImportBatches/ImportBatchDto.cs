namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class ImportBatchDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public int PageCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = null!;
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int DecodeIntegrityFailedCount { get; set; }
    public int DecodeIntegrityWarningCount { get; set; }
    public int VoucherCount { get; set; }
}
