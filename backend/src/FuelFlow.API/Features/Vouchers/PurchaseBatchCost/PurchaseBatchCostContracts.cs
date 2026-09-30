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

/// <summary>
/// Operator per-batch profit &amp; loss for one fuel (партія) within an import (pricing epic slice 2b):
/// what came in, what has sold (realized), and the margin still sitting in remaining stock (unrealized).
/// Costs/margins are internal only — never shown to customers.
/// </summary>
public sealed class ImportBatchPnlDto
{
    public string FuelTypeId { get; set; } = null!;
    public string? FuelTypeName { get; set; }
    public string Provider { get; set; } = null!;

    /// <summary>Everything in the batch (all statuses, excluding deleted).</summary>
    public int VouchersIn { get; set; }
    public decimal LitersIn { get; set; }

    /// <summary>Fulfilled to a non-reversed (not refunded/cancelled) order.</summary>
    public int VouchersSold { get; set; }
    public decimal LitersSold { get; set; }

    /// <summary>Still owned and sellable (Imported / VerifiedWithWarnings / Available).</summary>
    public int VouchersRemaining { get; set; }
    public decimal LitersRemaining { get; set; }

    /// <summary>Operator-owned stock that lapsed unsold and was retired to <c>Expired</c> (slice 4) — never sold, so its cost is a realised loss.</summary>
    public int VouchersExpired { get; set; }
    public decimal LitersExpired { get; set; }

    /// <summary>Cost/liter entered for THIS batch, or null when not yet entered.</summary>
    public decimal? CostPerLiter { get; set; }

    /// <summary>Sum of the sold vouchers' per-voucher sale price (UAH, to the kopeck); nets out fully refunded/cancelled orders.</summary>
    public decimal RealizedRevenue { get; set; }

    /// <summary>Cost of the sold liters (LitersSold × CostPerLiter); null when the batch is uncosted.</summary>
    public decimal? RealizedCogs { get; set; }

    /// <summary>RealizedRevenue − RealizedCogs; null when the batch is uncosted.</summary>
    public decimal? RealizedMargin { get; set; }

    /// <summary>RealizedRevenue / LitersSold; null when nothing has sold.</summary>
    public decimal? AvgSalePricePerLiter { get; set; }

    /// <summary>Current sale price/liter for the fuel (packages' FinalPricePerLiter); null if unpriced.</summary>
    public decimal? CurrentPricePerLiter { get; set; }

    /// <summary>Margin still to be earned on remaining stock at the current price: LitersRemaining × (CurrentPricePerLiter − CostPerLiter); null when cost or price is missing.</summary>
    public decimal? UnrealizedMargin { get; set; }

    /// <summary>Realised loss on the expired-unsold stock (LitersExpired × CostPerLiter); null when the batch is uncosted.</summary>
    public decimal? ExpiredLoss { get; set; }

    /// <summary>Bottom line actually realised on the batch so far: RealizedMargin − ExpiredLoss; null when the batch is uncosted.</summary>
    public decimal? NetRealizedResult { get; set; }
}
