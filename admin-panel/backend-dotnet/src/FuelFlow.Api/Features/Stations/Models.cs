namespace FuelFlow.Api.Features.Stations;

public record Station
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = "#00ff80";
    public string LogoText { get; init; } = string.Empty;
    public string? Lat { get; init; }
    public string? Lng { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateStationRequest
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = "#00ff80";
    public string LogoText { get; init; } = string.Empty;
}

public record UpdateStationRequest
{
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public string LogoText { get; init; } = string.Empty;
}
