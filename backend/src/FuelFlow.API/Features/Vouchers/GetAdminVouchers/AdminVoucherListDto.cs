namespace FuelFlow.Features.Vouchers.GetAdminVouchers;

public sealed class AdminVoucherListItemDto
{
    public Guid Id { get; set; }
    public string? QrPayload { get; set; }
    public decimal Liters { get; set; }
    public string FuelTypeId { get; set; } = null!;
    public FuelTypeRefDto? FuelType { get; set; }
    public string Provider { get; set; } = null!;
    public DateOnly ExpirationDate { get; set; }
    public string VoucherNumber { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
}

public sealed class FuelTypeRefDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public sealed class AdminVoucherListResponse
{
    public List<AdminVoucherListItemDto> Data { get; set; } = [];
    public int Total { get; set; }
    public int GlobalTotal { get; set; }
    public List<string> FuelTypes { get; set; } = [];
    public List<string> Providers { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<decimal> Amounts { get; set; } = [];
}
