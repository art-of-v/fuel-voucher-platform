namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackage
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public int Liters { get; set; }
    public int Price { get; set; }
    public int OriginalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
