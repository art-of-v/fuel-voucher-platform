namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Single source of truth for the customer-facing per-liter price (pricing epic,
/// slice 1a). Shared by the operator write paths in BrandsController
/// (recompute-and-store) and by checkout in ServerPricing (compute-fresh), so the
/// two can never drift.
/// </summary>
public static class FuelPricing
{
    /// <summary>
    /// The per-litre customer price, or <c>null</c> when the offer has no cost or no margin recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the function every read path calls</b>, and the reason is that it answers a question
    /// with a shape the callers can act on. <see cref="FinalPerLiter"/> takes four non-null decimals
    /// because the write paths have already decided a fuel is priced; a read path holding four nullable
    /// columns has to decide what "not priced yet" means, and every place that decided it for itself was
    /// a place the decision could differ. Measured on production 2026-10-17: 25 live offers, 122 live
    /// volumes, <b>0</b> of them without both knobs — so this returns null for a fuel the catalogue
    /// import created and nobody has priced yet, and for nothing else.
    /// </para>
    /// <para>
    /// Cost or margin missing ⇒ null rather than a number built from what is there. Treating a missing
    /// margin as zero would publish a free fuel; treating a missing cost as zero would let the
    /// below-cost guard see a cost of zero and wave the sale through.
    /// </para>
    /// </remarks>
    public static decimal? FinalPerLiterOrNull(
        decimal? costPerLiter,
        decimal? profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal? minDiscountPerLiter)
    {
        if (costPerLiter is not { } cost || profitPerLiter is not { } profit)
        {
            return null;
        }

        return FinalPerLiter(cost, profit, pumpPricePerLiter, minDiscountPerLiter ?? 0m);
    }

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
    /// True when the customer price/л the formula yields sits UNDER the supplier cost/л — i.e. the
    /// pump ceiling (pump − minDiscount) has forced the price below cost, so every liter sold loses
    /// money. Slice 3's hard block refuses such a sale unless a manager has opted the supplier+fuel
    /// in (deliberate loss-leader). Single source of truth so the operator write path, checkout, and
    /// the blended-cost reprice all judge "below cost" identically.
    /// <para>
    /// Only meaningful once a real cost is recorded: a fuel with no cost (<c>0</c>) is never
    /// below cost, so legacy / uncosted fuels are never blocked.
    /// </para>
    /// </summary>
    public static bool IsBelowCost(
        decimal costPerLiter,
        decimal profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal minDiscountPerLiter)
        => costPerLiter > 0m
           && FinalPerLiter(costPerLiter, profitPerLiter, pumpPricePerLiter, minDiscountPerLiter) < costPerLiter;

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
    ///
    /// Money is decimal UAH (spec §12, #90): the total is rounded to 2 decimals
    /// (kopecks) half-away-from-zero, matching the per-line rounding used at checkout.
    /// </summary>
    public static decimal OriginalPackagePrice(decimal? pumpPricePerLiter, decimal liters, decimal salePrice)
    {
        if (pumpPricePerLiter is { } pump)
        {
            return Math.Round(pump * liters, 2, MidpointRounding.AwayFromZero);
        }
        return salePrice;
    }
}
