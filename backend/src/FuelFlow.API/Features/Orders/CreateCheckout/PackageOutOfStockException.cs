namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout was refused because the package itself has no sellable stock behind it — nothing activated
/// for that brand + fuel + nominal in this customer's pool.
/// </summary>
/// <remarks>
/// <para>
/// A deliberate refusal, not a validation error: HTTP 409 with a stable <see cref="Code"/>, the same shape
/// as <see cref="BelowCostSaleBlockedException"/> and <see cref="TermStockUnavailableException"/>.
/// </para>
/// <para>
/// Historically a purchase could be accepted with zero inventory and left waiting for the operator to
/// restock — the customer had paid for fuel nobody could hand them, and the order sat
/// <c>PendingFulfillment</c> indefinitely with a Telegram alert nobody could act on. That trade-off was
/// deliberate when stock and catalog were independent (see the test plan's superseded S5), but the shop now
/// shows availability up front, so accepting the sale anyway would contradict what the customer was just
/// shown. Failing here also means the inventory alert that already fires on an unfulfillable order is
/// reserved for what it is actually for: stock that ran out <em>after</em> the customer was told it was there.
/// </para>
/// <para>
/// Not the same code family as the renewal-side <c>no_stock</c>: that one refuses a replacement of a
/// voucher the customer already holds, this one refuses a fresh purchase from an empty shelf. They are
/// separate codes on purpose — do not merge them.
/// </para>
/// </remarks>
public sealed class PackageOutOfStockException : Exception
{
    public const string Code = "package_out_of_stock";

    public PackageOutOfStockException(string BrandId, string fuelTypeId, decimal liters)
        : base($"Fuel {fuelTypeId} at {BrandId} for {liters:0.##}L has no stock available and cannot be bought.")
    {
    }
}