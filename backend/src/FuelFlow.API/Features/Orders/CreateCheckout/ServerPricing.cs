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
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter.HasValue)
        {
            return Math.Round(perLiter.Value * liters, 2, MidpointRounding.AwayFromZero);
        }

        return package.Price;
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
}
