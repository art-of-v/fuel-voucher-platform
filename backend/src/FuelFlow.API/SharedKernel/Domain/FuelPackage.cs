namespace FuelFlow.SharedKernel.Domain;

public sealed class FuelPackage
{
    public string Id { get; set; } = null!;
    public string StationId { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public decimal Price { get; set; }
    public decimal OriginalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Per-liter pricing (new pricing system)
    public decimal? SupplierPricePerLiter { get; set; }   // cost/л (собівартість)
    public decimal? MarginUahPerLiter { get; set; }       // profit/л (заробіток)
    public decimal? MarginPercent { get; set; }
    public decimal? FinalPricePerLiter { get; set; }      // computed: see FuelPricing.FinalPerLiter

    // Slice 1a pricing knobs. Pump (колонка) is the ceiling; MinDiscount is the
    // guaranteed gap under it. final = min(cost + profit, pump − minDiscount).
    // Pump null ⇒ fall back to cost + profit (legacy cost-plus).
    public decimal? PumpPricePerLiter { get; set; }
    public decimal? MinDiscountPerLiter { get; set; }

    public DateTime? PriceUpdatedAt { get; set; }
    public Guid? PriceUpdatedByUserId { get; set; }

    /// <summary>
    /// Archive, not delete. Deleting a catalog entry that fuel has already been sold against would
    /// cascade into the vouchers - real, paid-for paper - and destroy the only record of what a
    /// customer bought (#217). A used entry is soft-deleted: hidden from the catalog, still there for
    /// reports and for every historical row that points at it.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>When the entry was archived, or null while it is live.</summary>
    public DateTime? DeletedAtUtc { get; set; }
}
