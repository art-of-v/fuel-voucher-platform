using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Server-authoritative pricing for checkout. The client never dictates order prices;
/// every amount is derived from the persisted <see cref="FuelPackage"/> catalog.
/// Recomputes the per-liter price fresh from the stored pricing knobs via
/// <see cref="FuelPricing.FinalPerLiter"/> so a checkout always reflects the current
/// formula, then Price = round(final * liters), in whole hryvnia.
/// </summary>
public static class ServerPricing
{
    public static int PackagePrice(FuelPackage package, decimal liters)
    {
        var perLiter = EffectivePricePerLiter(package);
        if (perLiter.HasValue)
        {
            return (int)Math.Round(perLiter.Value * liters);
        }

        return package.Price;
    }

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
