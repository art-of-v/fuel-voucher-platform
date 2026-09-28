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

    /// <summary>
    /// The public "before" / struck-through reference price for a package: the pump
    /// price (колонка) × liters — the ceiling the customer's price sits below, i.e. the
    /// saving they get. Falls back to <paramref name="salePrice"/> when no pump price is
    /// recorded, so the store-front never fabricates a saving.
    ///
    /// This deliberately REPLACES the old <c>OriginalPrice = cost × liters</c>, which
    /// leaked the operator's supplier cost to anonymous callers and rendered the struck
    /// price BELOW the sale price (planning #73; the #52 fix dropped the raw cost/margin
    /// fields but left this one). The pump price is the public list price — safe to show.
    /// </summary>
    public static int OriginalPackagePrice(decimal? pumpPricePerLiter, decimal liters, int salePrice)
    {
        if (pumpPricePerLiter is { } pump)
        {
            return (int)Math.Round(pump * liters);
        }
        return salePrice;
    }
}
