namespace FuelFlow.Api.Features.FuelTypes;

public record FuelType
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public int BasePrice { get; init; }
    public int DiscountPrice { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateFuelTypeRequest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public int BasePrice { get; init; }
    public int DiscountPrice { get; init; }
}

public record UpdateFuelTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public int BasePrice { get; init; }
    public int DiscountPrice { get; init; }
}
