using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Server-authoritative pricing for checkout. The client never dictates order prices;
/// every amount is derived from the persisted catalogue - the fuel's per-litre facts, joined to the
/// volume being sold (<see cref="SellablePackage"/>). Recomputes the per-litre price fresh from the knobs via
/// <see cref="FuelPricing.FinalPerLiter"/> so a checkout always reflects the current
/// formula, then Price = round(final * liters, 2 dp) — decimal UAH to the kopeck
/// (spec §12, #90; rounds half away from zero).
/// <para>
/// <b>The below-cost check is asked about the price that will actually be charged</b>, not about the
/// rate the formula yields: a term discount is taken off the per-litre price before the line is
/// priced, so the guard has to see the discounted figure or a generous tier would be the one way to
/// sell under cost without the block firing.
/// </para>
/// </summary>
public static class ServerPricing
{
    public static decimal PackagePrice(SellablePackage package, decimal liters)
    {
        return PackagePrice(package, liters, 0m);
    }

    /// <summary>
    /// Package price with a short-term discount applied.
    /// </summary>
    /// <param name="discountPerLiter">
    /// UAH off the per-litre price for the term the customer bought. Never allowed to take the price
    /// below zero, and deliberately applied *after* the pump cap — so the term discount can narrow the
    /// customer's saving versus the pump but never makes them pay more than the pump price. The
    /// below-cost guard in the checkout handlers still runs on top, so a discount large enough to sell
    /// under cost is rejected there rather than silently accepted.
    /// </param>
    /// <exception cref="NoPriceAvailableException">
    /// The fuel has no price to charge: the catalogue published its pump price, but nobody has set a
    /// supplier cost and a margin, so the formula has nothing to compute from.
    /// </exception>
    public static decimal PackagePrice(SellablePackage package, decimal liters, decimal discountPerLiter)
    {
        return TryPackagePrice(package, liters, discountPerLiter)
            ?? throw new NoPriceAvailableException(
                "The fuel has no supplier cost and no margin, so there is no price to charge.");
    }

    /// <summary>
    /// The line price, or <c>null</c> when the fuel has no price to charge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nullable twin of <see cref="PackagePrice(SellablePackage, decimal, decimal)"/>, for the
    /// callers that can meet a fuel nobody has priced yet and have to decide what to SHOW rather than
    /// fail. The term quote is one of them: the storefront already lists such a fuel as unavailable,
    /// so the quote has to agree with it instead of faulting the customer's screen.
    /// </para>
    /// <para>
    /// One arithmetic, two shapes - the number comes from the same expression either way, so a quoted
    /// price and a charged price cannot drift.
    /// </para>
    /// </remarks>
    public static decimal? TryPackagePrice(SellablePackage package, decimal liters, decimal discountPerLiter)
    {
        // The per-litre price is the base the term discount comes off, so a discounted line and an
        // undiscounted one are the same number minus the discount - never a second formula.
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter is not { } price)
        {
            return null;
        }

        var discounted = Math.Max(0m, price - Math.Max(0m, discountPerLiter));
        return Math.Round(discounted * liters, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Pump-based list total for a line, frozen at checkout so the customer's saving vs the pump
    /// can be shown later without reconstructing a historical pump price. When the package carries
    /// no pump price this equals the amount charged (<paramref name="unitPrice"/> × quantity), i.e.
    /// zero saving. See <see cref="FuelPricing.OriginalPackagePrice"/>.
    /// </summary>
    public static decimal OriginalLineTotal(SellablePackage package, decimal liters, decimal unitPrice, int quantity)
        => FuelPricing.OriginalPackagePrice(package.PumpPricePerLiter, liters, unitPrice) * quantity;

    /// <summary>
    /// Per-liter customer price recomputed from the stored knobs, or <c>null</c> when the fuel has no
    /// cost or no margin recorded.
    /// <para>
    /// There is no longer a stored per-litre figure to fall back to. Before 2026-10-17 this returned
    /// <c>package.FinalPricePerLiter</c> when the knobs were incomplete, and that column was itself
    /// computed from the knobs by every write path — so it could only ever have been a stale copy of the
    /// same inputs, not an independent price. Reads now compute, and a null is the honest answer: a
    /// fuel nobody has costed has no price, and the callers decide what to show for it rather than
    /// the price function inventing one.
    /// </para>
    /// </summary>
    private static decimal? EffectivePricePerLiter(SellablePackage package)
        => package.ComputedFinalPricePerLiter;

    /// <summary>
    /// The per-litre price actually charged after a term discount, or null when the discount cannot be
    /// expressed against a stored per-litre price.
    /// </summary>
    /// <remarks>
    /// Exposed so the below-cost guard can be evaluated against the figure the customer is actually
    /// charged. Recomputing the price from cost + margin would never see a term discount — so a
    /// generous tier would have been able to sell under cost while the guard reported the line was
    /// fine.
    /// </remarks>
    public static decimal? DiscountedPricePerLiter(SellablePackage package, decimal discountPerLiter)
    {
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter is not { } price)
        {
            return null;
        }

        return Math.Max(0m, price - Math.Max(0m, discountPerLiter));
    }

    /// <summary>
    /// Whether charging <paramref name="unitPrice"/> for this package would sell under its supplier
    /// cost. Used by checkout with the term discount already applied.
    /// <para>
    /// The comparison is always per litre against per litre. A package with no stored per-litre
    /// price used to fall back to <see cref="FuelPackage.Price"/> — the frozen PACKAGE TOTAL — which
    /// is a different unit entirely: 590 UAH for a 50 L package read as "590 vs a 54 UAH/litre cost"
    /// and came out looking profitable while the customer was being charged 11.80/litre against a
    /// 54/litre cost. That fallback is gone. With no per-litre price the guard has nothing sound to
    /// compare, so it reports "unknown" via <see cref="TryGetBelowCostVerdict"/> rather than
    /// guessing in the direction that loses money.
    /// </para>
    /// </summary>
    public static bool IsBelowCost(SellablePackage package, decimal discountPerLiter)
        => TryGetBelowCostVerdict(package, discountPerLiter) == BelowCostVerdict.BelowCost;

    /// <summary>Outcome of the below-cost check: whether the line sells under cost, or "cannot tell".</summary>
    public enum BelowCostVerdict
    {
        /// <summary>No stored supplier cost, so there is nothing to compare against.</summary>
        UnknownCost,

        /// <summary>The per-litre customer price sits under the per-litre supplier cost.</summary>
        BelowCost,

        /// <summary>Soundly at or above cost.</summary>
        AboveCost,
    }

    /// <summary>
    /// The below-cost check with its ignorance made explicit, so a caller that is about to sell
    /// without a recorded cost can log it instead of treating silence as permission.
    /// </summary>
    public static BelowCostVerdict TryGetBelowCostVerdict(SellablePackage package, decimal discountPerLiter)
    {
        // Zero and negative costs are "not recorded", not "free": a package carrying 0 was never
        // priced, and treating it as free would make every line look loss-making and block the sale.
        if (package.SupplierPricePerLiter is not { } cost || cost <= 0m)
        {
            return BelowCostVerdict.UnknownCost;
        }

        // The guard is asked about the price that will actually be charged - the per-litre rate after
        // the term discount - rather than about the rate the formula would yield. Without this the
        // discount would be invisible to it, and a generous tier would be the one way to sell under
        // cost with the block green.
        var chargedPerLiter = DiscountedPricePerLiter(package, discountPerLiter);
        if (chargedPerLiter is not { } price)
        {
            return BelowCostVerdict.UnknownCost;
        }

        return price < cost ? BelowCostVerdict.BelowCost : BelowCostVerdict.AboveCost;
    }
}
