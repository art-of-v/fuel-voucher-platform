namespace FuelFlow.Api.Features.FuelPackages;

public record FuelPackage
{
    public string Id { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string FuelTypeId { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
    public int OriginalPrice { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateFuelPackageRequest
{
    public string Id { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string FuelTypeId { get; init; } = string.Empty;
    public string FuelName { get; init; } = string.Empty;
    public int Liters { get; init; }
    public int Price { get; init; }
    public int OriginalPrice { get; init; }
}
