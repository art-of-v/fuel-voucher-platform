namespace FuelFlow.Api.Features.Vouchers;

public record Voucher
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public string? FuelSubtype { get; init; }
    public int Amount { get; init; }
    public string? QrCodeData { get; init; }
    public string? ImageUrl { get; init; }
    public string? ExternalId { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public string Status { get; init; } = "imported";
    public string Unit { get; init; } = "liters";
    public string? OriginalFileName { get; init; }
    public string? Source { get; init; }
    public string? ImportJobId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record InventoryAggregation
{
    public string Provider { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int AvailableCount { get; init; }
}
