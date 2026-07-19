namespace FuelFlow.SharedKernel.Domain;

public sealed class Station
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Color { get; set; } = "#00ff80";
    public string LogoText { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? StationType { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
