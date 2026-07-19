namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelTypeEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public int BasePrice { get; set; }
    public int DiscountPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
