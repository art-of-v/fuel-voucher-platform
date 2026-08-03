using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// Server-authoritative pricing for checkout. The client never dictates order prices;
/// every amount is derived from the persisted <see cref="FuelPackage"/> catalog.
/// Mirrors the formula used when packages are created/updated in ProvidersController:
/// Price = round(FinalPricePerLiter * liters), in whole hryvnia.
/// </summary>
public static class ServerPricing
{
    public static int PackagePrice(FuelPackage package, decimal liters)
    {
        if (package.FinalPricePerLiter.HasValue)
        {
            return (int)Math.Round(package.FinalPricePerLiter.Value * liters);
        }

        return package.Price;
    }
}
