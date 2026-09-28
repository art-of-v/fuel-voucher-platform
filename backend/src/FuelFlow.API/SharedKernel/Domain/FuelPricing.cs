namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Single source of truth for the customer-facing per-liter price (pricing epic,
/// slice 1a). Shared by the operator write paths in ProvidersController
/// (recompute-and-store) and by checkout in ServerPricing (compute-fresh), so the
/// two can never drift.
/// </summary>
public static class FuelPricing
{
    /// <summary>
    /// final/л = min(cost + profit, pump − minDiscount).
    /// <paramref name="pumpPricePerLiter"/> (колонка) is the hard ceiling; when it is
    /// unknown (null) we fall back to cost + profit so a fuel priced before a pump was
    /// ever recorded keeps its legacy cost-plus price.
    /// </summary>
    public static decimal FinalPerLiter(
        decimal costPerLiter,
        decimal profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal minDiscountPerLiter)
    {
        var costPlus = costPerLiter + profitPerLiter;
        if (pumpPricePerLiter is { } pump)
        {
            return Math.Min(costPlus, pump - minDiscountPerLiter);
        }
        return costPlus;
    }
}
