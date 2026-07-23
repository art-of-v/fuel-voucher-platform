namespace FuelFlow.SharedKernel.DTOs;

public sealed class VoucherDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public decimal Amount { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string QrPayload { get; set; } = null!;
    public string QrCodeData { get; set; } = null!;
    public string Status { get; set; } = null!;
}
