namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Blended cost for the pricing engine (pricing epic slice 2a, spec §1).
/// </summary>
public static class PurchaseBatchCosting
{
    /// <summary>
    /// Perpetual moving weighted-average cost of the in-stock pool for one supplier+fuel.
    /// </summary>
    /// <remarks>
    /// The textbook perpetual moving average (<c>new_avg = (pool_value + batch_value) /
    /// (pool_liters + batch_liters)</c>, updated only on batch load) equals the
    /// <b>remaining-liters-weighted mean</b> over the cost-bearing batches still in stock:
    /// <c>Σ(remainingLiters_b × cost_b) / Σ(remainingLiters_b)</c>. Computing it from live
    /// stock this way is order-independent and needs no running-average state. It is
    /// sales-insensitive in the sense that matters: a sale removes that voucher's liters at
    /// the current average — it never moves the per-liter number — and we only ever recompute
    /// on batch cost entry, so the price "steps" solely on batch load.
    /// Returns <c>null</c> when the pool holds no cost-bearing liters (nothing to average).
    /// </remarks>
    public static decimal? BlendedCostPerLiter(IEnumerable<(decimal liters, decimal costPerLiter)> pool)
    {
        decimal totalLiters = 0m;
        decimal totalValue = 0m;
        foreach (var (liters, costPerLiter) in pool)
        {
            if (liters <= 0m) continue;
            totalLiters += liters;
            totalValue += liters * costPerLiter;
        }

        return totalLiters > 0m ? totalValue / totalLiters : null;
    }
}
