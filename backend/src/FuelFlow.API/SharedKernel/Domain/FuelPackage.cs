namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackage
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Price { get; set; }
    public int OriginalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Per-liter pricing (new pricing system)
    public decimal? SupplierPricePerLiter { get; set; }
    public decimal? MarginUahPerLiter { get; set; }
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }
    public DateTime? PriceUpdatedAt { get; set; }
    public Guid? PriceUpdatedByUserId { get; set; }
}
