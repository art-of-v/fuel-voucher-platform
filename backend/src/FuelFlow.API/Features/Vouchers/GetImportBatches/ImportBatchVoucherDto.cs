namespace FuelFlow.Features.Vouchers.GetImportBatches;

public sealed class ImportBatchVoucherDto
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = null!;
    public Guid FuelTypeId { get; set; }
    public string? FuelTypeName { get; set; }
    public decimal Liters { get; set; }
    /// <summary>Real term printed on the supplier's document - the ceiling for this stock.</summary>
    public DateOnly SupplierExpirationDate { get; set; }
    /// <summary>What the customer was sold; differs only once short terms ship.</summary>
    public DateOnly CustomerExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public decimal? MatrixMismatchPercent { get; set; }
    public int? MatrixMismatchedModules { get; set; }
    public int? MatrixTotalModules { get; set; }
}
