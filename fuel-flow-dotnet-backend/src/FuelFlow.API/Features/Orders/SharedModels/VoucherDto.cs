namespace FuelFlow.API.Features.Orders.SharedModels;

public sealed class VoucherDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public decimal Liters { get; set; }
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string QrPayload { get; set; } = null!;
    public string Status { get; set; } = null!;
}