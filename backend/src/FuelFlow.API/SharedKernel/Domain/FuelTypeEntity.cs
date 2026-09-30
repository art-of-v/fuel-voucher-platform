namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelTypeEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public decimal DiscountPrice { get; set; }

    /// <summary>
    /// Manager standing opt-in: allow this supplier+fuel to be priced and sold BELOW blended
    /// cost (deliberate loss-leader). Default false, so the slice-3 hard block refuses below-cost
    /// pricing/sales until a manager consciously flips this. A per supplier+fuel decision — the
    /// fuel type id already encodes supplier+fuel (e.g. "okko-dp").
    /// </summary>
    public bool AllowBelowCost { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
