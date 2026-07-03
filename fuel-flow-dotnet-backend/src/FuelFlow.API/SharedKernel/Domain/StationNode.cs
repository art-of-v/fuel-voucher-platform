namespace FuelFlow.SharedKernel.Domain;

public sealed class StationNode
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? StationType { get; set; }
    public string? Lat { get; set; }
    public string? Lng { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
