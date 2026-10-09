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

    /// <summary>
    /// The fuel that blocked the sale, kept as a property for logs and telemetry rather than
    /// interpolated into <see cref="Exception.Message"/>.
    /// </summary>
    public string FuelTypeId { get; }

    /// <param name="fuelTypeId">
    /// The fuel's identifier. It lands in logs, never in the message: <see cref="Message"/> reaches
    /// the customer's screen verbatim on any client that has not mapped <see cref="Code"/> yet, and
    /// an internal GUID there explains nothing to the person holding the phone. It also said
    /// "priced below supplier cost", which is our margin decision, not an explanation of anything
    /// the customer did or can do.
    /// </param>
    public BelowCostSaleBlockedException(string fuelTypeId)
        : base("This fuel is currently unavailable for purchase.")
    {
        FuelTypeId = fuelTypeId;
    }
}