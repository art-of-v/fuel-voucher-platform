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
    /// The per-litre customer price, or <c>null</c> when the fuel has no cost or no margin recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the function every read path calls</b>, and the reason is that it answers a question
    /// with a shape the callers can act on. <see cref="FinalPerLiter"/> takes four non-null decimals
    /// because the write paths have already decided a fuel is priced; a read path holding four nullable
    /// columns has to decide what "not priced yet" means, and every place that decided it for itself was
    /// a place the decision could differ. Measured on production 2026-10-17, when the price still
    /// lived on <c>fuel_offers</c> and its volumes: 25 live offers, 122 live volumes, <b>0</b> of them
    /// without both knobs — so this returns null for a fuel the catalogue import created and nobody
    /// has priced yet, and for nothing else.
    /// </para>
    /// <para>
    /// Cost or margin missing ⇒ null rather than a number built from what is there. Treating a missing
    /// margin as zero would publish a free fuel; treating a missing cost as zero would let the
    /// below-cost guard see a cost of zero and wave the sale through.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The per-litre customer price, or <c>null</c> when the fuel has no cost or no margin recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the function every read path calls</b>, and the reason is that it answers a question
    /// with a shape the callers can act on. <see cref="FinalPerLiter"/> takes three non-null decimals
    /// because the write paths have already decided a fuel is priced; a read path holding two nullable
    /// columns has to decide what "not priced yet" means, and every place that decided it for itself was
    /// a place the decision could differ. Measured on production 2026-10-17, when the price still
    /// lived on <c>fuel_offers</c> and its volumes: 25 live offers, 122 live volumes, <b>0</b> of them
    /// without both knobs — so this returns null for a fuel the catalogue import created and nobody
    /// has priced yet, and for nothing else.
    /// </para>
    /// <para>
    /// Cost or margin missing ⇒ null rather than a number built from what is there. Treating a missing
    /// margin as zero would publish a free fuel; treating a missing cost as zero would let the
    /// below-cost guard see a cost of zero and wave the sale through.
    /// </para>
    /// </remarks>
    public static decimal? FinalPerLiterOrNull(
        decimal? costPerLiter,
        decimal? profitPerLiter)
    {
        if (costPerLiter is not { } cost || profitPerLiter is not { } profit)
        {
            return null;
        }

        return FinalPerLiter(cost, profit);
    }

    /// <summary>
    /// The formula as it was BEFORE the minimum-discount term was removed, kept frozen for the
    /// migration history.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A migration that has run must produce the rows it produced then. The catalogue seed of
    /// 2026-10-15 wrote package totals through this formula, and a formula that moved under it would
    /// write different numbers into a database that already holds the old ones — a history that no
    /// longer reproduces itself. So the old shape stays, unchanged, for the migrations that call it,
    /// and the live formula is the three-argument one above.
    /// </para>
    /// <para>
    /// <b>Nothing in the running application calls this.</b> It exists for the migration history and
    /// for the two dead types the history needs to compile.
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

        var costPlus = cost + profit;
        if (pumpPricePerLiter is { } pump)
        {
            return Math.Min(costPlus, pump - (minDiscountPerLiter ?? 0m));
        }
        return costPlus;
    }

    /// <summary>
    /// our price/л = cost + profit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The column price is not a term of this formula.</b> It is what the customer is shown
    /// <em>against</em> - the reference the saving is measured from - and never the second term of
    /// a formula. The owner's model (planning <c>docs/PRICING_MODEL.md</c>, 2026-10-21): *the price
    /// of a litre is the cost we bought it for plus our margin*.
    /// </para>
    /// <para>
    /// <b>There is no minimum-discount term either</b>, and that is the same decision: the knob that
    /// used to sit under the column was a second dial on the same number, and four different writers
    /// maintained the copies of it. It was also the one that reached a stored setting from a display
    /// number - the admin card fell back to the displayed saving when the knob was unset, so a fuel
    /// whose stored price was zero got "the price is at least the whole column price under the
    /// column" written into it, and the formula then produced zero.
    /// </para>
    /// <para>
    /// The consequence to know: our price can now be <b>above</b> the column price, and the customer
    /// sees that as it is. A market that falls below what we pay refuses nothing - the column price
    /// is a reference, not a term of our price.
    /// </para>
    /// <para>
    /// The guard that can still lose us money is at checkout, and it judges the price the customer is
    /// <b>actually charged</b> - our price minus the term discount - against our cost
    /// (<c>ServerPricing.TryGetBelowCostVerdict</c>). A generous term on the ladder can take a sale
    /// under what we paid; the market falling cannot. <c>allow_below_cost</c> is what disarms it for
    /// a deliberate loss leader.
    /// </para>
    /// </remarks>
    public static decimal FinalPerLiter(
        decimal costPerLiter,
        decimal profitPerLiter)
    {
        return costPerLiter + profitPerLiter;
    }

    /// <summary>
    /// The public "before" / struck-through reference price for a package: the pump
    /// price (колонка) × liters — the price the customer is shown <em>against</em>, i.e. the
    /// saving they get when it sits above what we charge. Falls back to
    /// <paramref name="salePrice"/> when no pump price is recorded, so the store-front never
    /// fabricates a saving.
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
    /// What a nominal costs: the fuel's per-litre price times the size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One function, because a shelf price and a charged price that disagree are the bug this
    /// column pair used to be able to cause.</b> The storefront, the basket, the quote and the charge all
    /// come through here, so "what the customer sees" and "what the customer is charged" cannot drift:
    /// there is one expression and every caller uses it.
    /// </para>
    /// <para>
    /// <b>The rate is the fuel's, and the size only multiplies it.</b> The knobs that decide the
    /// price - supplier cost and margin - live on the fuel, and the column price beside them is the
    /// reference the customer is shown against; every size of that fuel is priced by the same
    /// rate. There is no per-nominal override any more: the one-off prices went with the offers on
    /// 2026-10-18 (<c>20261018190514_NoOneOffPrices</c>), and with them the question of what a
    /// deliberate price for one size would survive a repricing.
    /// </para>
    /// <para>
    /// Rounded to the kopeck half away from zero, matching per-line rounding at checkout. Money is
    /// decimal UAH throughout (spec §12).
    /// </para>
    /// </remarks>
    /// <param name="costPerLiter">The fuel's supplier cost, or null when the fuel is not priced yet.</param>
    /// <param name="profitPerLiter">The fuel's margin, or null when the fuel is not priced yet.</param>
    /// <param name="pumpPricePerLiter">The column price (колонка) the caller has on file. It is not a
    /// term of the price - the rate is the cost plus the margin - and the parameter is no longer read.</param>
    /// <param name="liters">The nominal being priced.</param>
    public static decimal PackagePrice(
        decimal? costPerLiter,
        decimal? profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal liters)
        => TryPackagePrice(costPerLiter, profitPerLiter, pumpPricePerLiter, liters)
           ?? throw new NoPriceAvailableException(
               "The fuel has no supplier cost or no margin, so there is no price to quote.");

    /// <summary>
    /// The nullable form of <see cref="PackagePrice"/>, for the callers that can meet a nominal with no
    /// price and have to decide what to do about it rather than fail.
    /// </summary>
    /// <remarks>
    /// Three callers, three answers, which is why this is separate from the throwing one. The storefront
    /// lists such a fuel at the pump price the market publishes and marks it unavailable: it cannot be
    /// bought, and a zero on the shelf is a free fuel. The term quote answers with the ladder and no
    /// prices, for the same reason a missing package gets one. Checkout throws, because charging
    /// nothing is not an option.
    /// </remarks>
    public static decimal? TryPackagePrice(
        decimal? costPerLiter,
        decimal? profitPerLiter,
        decimal? pumpPricePerLiter,
        decimal liters)
    {
        if (FinalPerLiterOrNull(costPerLiter, profitPerLiter) is not { } price)
        {
            return null;
        }

        return Math.Round(price * liters, 2, MidpointRounding.AwayFromZero);
    }
}
