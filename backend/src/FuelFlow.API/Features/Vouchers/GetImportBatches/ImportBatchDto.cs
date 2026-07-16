namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class ImportBatchDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public int PageCount { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = null!;
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int FailedCount { get; set; }
    public int VerificationFailedCount { get; set; }
    public int VerifiedWithWarningsCount { get; set; }
    public int VoucherCount { get; set; }
}
