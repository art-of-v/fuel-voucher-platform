namespace FuelFlow.Api.Features.QrCodes;

public record QrCode
{
    public int Id { get; init; }
    public string StationId { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public int Liters { get; init; }
    public string QrCodeUrl { get; init; } = string.Empty;
    public string Status { get; init; } = "available";
    public int? PurchaseId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateQrCodeRequest
{
    public string StationId { get; init; } = string.Empty;
    public string FuelType { get; init; } = string.Empty;
    public int Liters { get; init; }
    public string QrCodeUrl { get; init; } = string.Empty;
}
