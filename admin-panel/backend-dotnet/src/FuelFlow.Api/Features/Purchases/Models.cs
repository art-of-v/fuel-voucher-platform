namespace FuelFlow.Api.Features.Purchases;

public record Purchase
{
    public int Id { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
    public int? QrCodeId { get; init; }
    public Guid? VoucherId { get; init; }
    public string Status { get; init; } = "pending";
    public string? StripeSessionId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreatePurchaseRequest
{
    public string SessionId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
}

public record PurchaseWithQrCode
{
    public int Id { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
    public string Status { get; init; } = "pending";
    public string? QrCodeUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}
