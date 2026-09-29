namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout was refused because the fuel's customer price/л sits below its blended supplier
/// cost/л (pricing epic slice 3 hard block) and no manager has opted the supplier+fuel in as a
/// deliberate loss-leader. Surfaces as HTTP 409 with a stable <see cref="Code"/> plus a human
/// message — distinct from validation (400), an inactive account (403) and unexpected faults (500).
/// </summary>
public sealed class BelowCostSaleBlockedException : Exception
{
    public const string Code = "below_cost";

    public BelowCostSaleBlockedException(string fuelTypeId)
        : base($"Fuel {fuelTypeId} is priced below supplier cost and cannot be sold.")
    {
    }
}
