namespace FuelFlow.SharedKernel.DTOs;

public sealed class PurchaseDto
{
    public Guid Id { get; set; }
    public string ProductType { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public int Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankStatus { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public List<VoucherDto>? Vouchers { get; set; }
}
