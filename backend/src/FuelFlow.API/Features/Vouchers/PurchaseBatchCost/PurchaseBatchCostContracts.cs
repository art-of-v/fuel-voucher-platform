namespace FuelFlow.Features.Vouchers.PurchaseBatchCost;

/// <summary>
/// One fuel type (партія) within an import, rolled up with its manually-entered cost/liter (if any)
/// and the current blended cost for the whole supplier+fuel pool (pricing epic slice 2a).
/// </summary>
public sealed class ImportBatchCostDto
{
    public string FuelTypeId { get; set; } = null!;
    public string? FuelTypeName { get; set; }
    public string Provider { get; set; } = null!;
    public int VoucherCount { get; set; }
    public decimal TotalLiters { get; set; }

    /// <summary>Cost/liter entered for THIS batch (import × fuel), or null when not yet entered.</summary>
    public decimal? CostPerLiter { get; set; }

    /// <summary>Current blended moving-average cost for the whole supplier+fuel pool (display only, never shown to customers).</summary>
    public decimal? BlendedCostPerLiter { get; set; }
}

/// <summary>PUT body for setting a batch's cost — cost per liter in UAH (собівартість).</summary>
public sealed class SetBatchCostRequest
{
    public decimal CostPerLiter { get; set; }
}
