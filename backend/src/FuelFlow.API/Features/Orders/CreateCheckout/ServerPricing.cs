using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Server-authoritative pricing for checkout. The client never dictates order prices;
/// every amount is derived from the persisted catalogue - the offer's per-litre facts, joined to the
/// volume being sold (<see cref="SellablePackage"/>). Recomputes the per-litre price fresh from the knobs via
/// <see cref="FuelPricing.FinalPerLiter"/> so a checkout always reflects the current
/// formula, then Price = round(final * liters, 2 dp) — decimal UAH to the kopeck
/// (spec §12, #90; rounds half away from zero).
/// <para>
/// <b>A per-nominal override wins over the formula here, and is guarded here.</b> That is the whole
/// point of making it an explicit, nullable column rather than a stored copy of the arithmetic: an
/// operator can price one size by hand, the customer is charged that, and the below-cost check below is
/// asked about the price that will actually be charged rather than about the rate the formula yields.
/// Without that, an override would be the one way to sell under cost without the block firing.
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
    public static decimal PackagePrice(SellablePackage package, decimal liters, decimal discountPerLiter)
    {
        // An override IS the price of this nominal, so it is the base the term discount comes off — not
        // a fallback for when the offer has no rate. Discounting the formula instead and ignoring the
        // override would make it a decoration: the shelf would show one number and the charge another.
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter.HasValue)
        {
            var discounted = Math.Max(0m, perLiter.Value - Math.Max(0m, discountPerLiter));
            return Math.Round(discounted * liters, 2, MidpointRounding.AwayFromZero);
        }

        // Neither an override nor a rate: there is nothing to charge. The checkout handlers resolve the
        // package before pricing it and never reach here for an unpriced fuel, so this is a statement
        // rather than a decision — and it refuses rather than quoting zero, because zero is a free fuel.
        throw new InvalidOperationException(
            "No per-litre price and no per-nominal override: there is nothing to charge.");
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
    /// Per-liter customer price recomputed from the stored knobs, or <c>null</c> when the offer has no
    /// cost or no margin recorded — in which case the caller falls back to the volume's own price
    /// (<see cref="SellablePackage.PackagePrice"/>, which by then is the per-nominal override).
    /// <para>
    /// There is no longer a stored per-litre figure to fall back to. Before 2026-10-17 this returned
    /// <c>package.FinalPricePerLiter</c> when the knobs were incomplete, and that column was itself
    /// computed from the knobs by every write path — so it could only ever have been a stale copy of the
    /// same inputs, not an independent price. Reads now compute, and the only honest fallback is the
    /// volume's own figure.
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
    /// charged. <see cref="FuelPricing.IsBelowCost(cost, profit, pump, minDiscount)"/> recomputes the
    /// price from cost + margin and would never see a term discount — so a generous tier would have been
    /// able to sell under cost while the guard reported the line was fine.
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

        // An override IS the charged price, so the guard is asked about it rather than about the rate
        // the formula would yield. Without this branch a per-nominal override would be the one route to
        // selling under cost with the block green, which is the entire reason the override is an explicit
        // column and not a second copy of the arithmetic.
        var chargedPerLiter = DiscountedPricePerLiter(package, discountPerLiter);
        if (chargedPerLiter is not { } price)
        {
            return BelowCostVerdict.UnknownCost;
        }

        return price < cost ? BelowCostVerdict.BelowCost : BelowCostVerdict.AboveCost;
    }
}
