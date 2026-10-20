namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout was refused because the fuel is in stock but nobody has recorded what we paid for
/// it, so the price cannot be compared against a cost.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT the same as <see cref="BelowCostSaleBlockedException"/>, and the difference is the whole point.
/// That one says "the price is below what we paid" — a fact, and a deliberate one to refuse. This one says
/// "we do not know what we paid", which is not a fact at all, and a refusal built on an unknown is not a
/// guarantee.
/// </para>
/// <para>
/// The below-cost guard used to be skipped in exactly this case, because it was written as
/// <c>package.SupplierPricePerLiter is { } cost &amp;&amp; …</c>: a missing cost meant the pattern did not
/// match, so the whole block was skipped and <b>the sale went through</b>. The model already had the honest
/// answer — <c>ServerPricing.TryGetBelowCostVerdict</c> returns <c>UnknownCost</c> for exactly this case — and
/// nobody asked it. A fuel with no recorded cost was therefore freely sellable, at whatever price the package
/// carried.
/// </para>
/// <para>
/// It matters because the catalogue import creates fuels with no cost on purpose: it knows the column price
/// (what the driver pays) and nothing about what we pay. Until an operator states the cost, those fuels must
/// not be sold. The storefront shows them at the column with "not in stock", which is a true statement; this
/// exception is what stops a stocked one from being sold at an unknown margin.
/// </para>
/// </remarks>
public sealed class UnknownCostSaleBlockedException : Exception
{
    public const string Code = "unknown_cost";

    /// <summary>
    /// The fuel that blocked the sale, kept as a property for logs and telemetry rather than
    /// interpolated into <see cref="Exception.Message"/> — the same reason as
    /// <see cref="BelowCostSaleBlockedException"/>.
    /// </summary>
    public Guid FuelTypeId { get; }

    /// <param name="fuelTypeId">The fuel's identifier. It lands in logs, never in the message.</param>
    public UnknownCostSaleBlockedException(Guid fuelTypeId)
        : base("This fuel is currently unavailable for purchase.")
    {
        FuelTypeId = fuelTypeId;
    }
}
