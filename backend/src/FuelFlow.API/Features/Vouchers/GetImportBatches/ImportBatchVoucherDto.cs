namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class ImportBatchVoucherDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string? FuelTypeName { get; set; }
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public double? VerificationMismatchPercent { get; set; }
    public int? VerificationMismatchedModules { get; set; }
    public int? VerificationTotalModules { get; set; }
}
