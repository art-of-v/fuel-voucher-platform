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

    /// <summary>
    /// What a nominal costs, from the offer's rate and the volume's optional override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One function, because a shelf price and a charged price that disagree are the bug this
    /// column pair used to be able to cause.</b> The storefront, the basket, the quote and the charge all
    /// come through here, so "what the customer sees" and "what the customer is charged" cannot drift:
    /// there is one expression and every caller uses it.
    /// </para>
    /// <para>
    /// <b>An override is an exception, and this is where it is honoured.</b> Null means "price it like
    /// the rest of the fuel" — the ordinary case, and the one every write path produces. A number means an
    /// operator set this nominal deliberately (a round marketing price for a 50 L canister, say), and it
    /// survives a repricing of the offer rather than being overwritten, which is what the old
    /// <c>package_price</c> column could not do.
    /// </para>
    /// <para>
    /// Rounded to the kopeck half away from zero, matching per-line rounding at checkout. Money is
    /// decimal UAH throughout (spec §12).
    /// </para>
    /// </remarks>
    /// <param name="costPerLiter">The offer's supplier cost, or null when the fuel is not priced yet.</param>
    /// <param name="profitPerLiter">The offer's margin, or null when the fuel is not priced yet.</param>
    /// <param name="pumpPricePerLiter">The pump ceiling (колонка), or null when none is recorded.</param>
    /// <param name="minDiscountPerLiter">The guaranteed gap under the ceiling, or null for zero.</param>
    /// <param name="liters">The nominal being priced.</param>
    /// <param name="overrideUah">
    /// This nominal's own price, when one was set. Wins over the arithmetic, including when it is lower —
    /// the below-cost guard, not this function, is where a price that loses money is refused.
    /// </param>
    public static decimal PackagePrice(
        decimal? costPerLiter,
        decimal? profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal? minDiscountPerLiter,
        decimal liters,
        decimal? overrideUah)
        => TryPackagePrice(costPerLiter, profitPerLiter, pumpPricePerLiter, minDiscountPerLiter, liters, overrideUah)
           ?? throw new InvalidOperationException(
               "The offer has no supplier cost or no margin, and this volume carries no price override, "
               + "so there is no price to quote.");

    /// <summary>
    /// The nullable form of <see cref="PackagePrice"/>, for the callers that can meet a nominal with no
    /// price and have to decide what to do about it rather than fail.
    /// </summary>
    /// <remarks>
    /// Three callers, three answers, which is why this is separate from the throwing one. The storefront
    /// skips such a package: it cannot be bought and a zero on the shelf is a free fuel. The admin
    /// listing shows it at zero, because an operator needs to see that the nominal exists and is not
    /// priced yet. Checkout throws, because charging nothing is not an option.
    /// </remarks>
    public static decimal? TryPackagePrice(
        decimal? costPerLiter,
        decimal? profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal? minDiscountPerLiter,
        decimal liters,
        decimal? overrideUah)
    {
        if (overrideUah is { } set)
        {
            return set;
        }

        if (FinalPerLiterOrNull(costPerLiter, profitPerLiter, pumpPricePerLiter, minDiscountPerLiter) is not { } price)
        {
            return null;
        }

        return Math.Round(price * liters, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Whether charging <paramref name="packagePrice"/> for this nominal would sell under its supplier
    /// cost — the same question <see cref="IsBelowCost(decimal,decimal,decimal?,decimal)"/> asks, but
    /// asked about the price that will actually be charged rather than about the rate the formula yields.
    /// </summary>
    /// <remarks>
    /// This is what makes an override subject to the same guard as everything else. Without it, a
    /// per-nominal override would be the one way to sell under cost without the block firing, because the
    /// guard would judge the rate while the customer was charged the override.
    /// </remarks>
    public static bool IsBelowCost(decimal? costPerLiter, decimal liters, decimal packagePrice)
        => costPerLiter is { } cost
           && cost > 0m
           && liters > 0m
           && packagePrice < cost * liters;
}
