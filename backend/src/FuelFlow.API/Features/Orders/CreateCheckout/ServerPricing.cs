using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Server-authoritative pricing for checkout. The client never dictates order prices;
/// every amount is derived from the persisted <see cref="FuelPackage"/> catalog.
/// Recomputes the per-liter price fresh from the stored pricing knobs via
/// <see cref="FuelPricing.FinalPerLiter"/> so a checkout always reflects the current
/// formula, then Price = round(final * liters, 2 dp) — decimal UAH to the kopeck
/// (spec §12, #90; rounds half away from zero).
/// </summary>
public static class ServerPricing
{
    public static decimal PackagePrice(FuelPackage package, decimal liters)
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
    public static decimal PackagePrice(FuelPackage package, decimal liters, decimal discountPerLiter)
    {
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter.HasValue)
        {
            var discounted = Math.Max(0m, perLiter.Value - Math.Max(0m, discountPerLiter));
            return Math.Round(discounted * liters, 2, MidpointRounding.AwayFromZero);
        }

        if (discountPerLiter <= 0m)
        {
            return package.Price;
        }

        // No per-liter price to discount against: scale the frozen package price by the same ratio the
        // discount represents on the nominal, so a term still costs less. Rows this old are rare and the
        // below-cost guard still applies.
        var nominalPerLiter = liters > 0m ? package.Price / liters : package.Price;
        var reduced = Math.Max(0m, nominalPerLiter - Math.Max(0m, discountPerLiter));
        return Math.Round(reduced * liters, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Pump-based list total for a line, frozen at checkout so the customer's saving vs the pump
    /// can be shown later without reconstructing a historical pump price. When the package carries
    /// no pump price this equals the amount charged (<paramref name="unitPrice"/> × quantity), i.e.
    /// zero saving. See <see cref="FuelPricing.OriginalPackagePrice"/>.
    /// </summary>
    public static decimal OriginalLineTotal(FuelPackage package, decimal liters, decimal unitPrice, int quantity)
        => FuelPricing.OriginalPackagePrice(package.PumpPricePerLiter, liters, unitPrice) * quantity;

    /// <summary>
    /// Per-liter customer price recomputed from the stored knobs. Falls back to the
    /// stored <see cref="FuelPackage.FinalPricePerLiter"/>, then null (caller uses the
    /// frozen <see cref="FuelPackage.Price"/>) for rows predating the per-liter columns.
    /// </summary>
    private static decimal? EffectivePricePerLiter(FuelPackage package)
    {
        if (package.SupplierPricePerLiter is { } cost && package.MarginUahPerLiter is { } profit)
        {
            return FuelPricing.FinalPerLiter(cost, profit, package.PumpPricePerLiter, package.MinDiscountPerLiter ?? 0m);
        }

        return package.FinalPricePerLiter;
    }

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
    public static decimal? DiscountedPricePerLiter(FuelPackage package, decimal discountPerLiter)
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
    /// </summary>
    public static bool IsBelowCost(FuelPackage package, decimal discountPerLiter)
        => package.SupplierPricePerLiter is { } cost
           && cost > 0m
           && (DiscountedPricePerLiter(package, discountPerLiter) ?? FinalPerLiterFallback(package)) < cost;

    /// <summary>
    /// Last-resort per-litre figure for a package with no stored per-litre columns, so the guard still
    /// has something to compare. Only reached for rows predating the pricing columns.
    /// </summary>
    private static decimal FinalPerLiterFallback(FuelPackage package)
        => package.FinalPricePerLiter ?? package.Price;
}
