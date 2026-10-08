namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout was refused because the line asked for a validity term no in-stock voucher can cover. The
/// station cannot deliver the term, so the sale must not complete at that term's (higher) price and then
/// hand back a shorter validity.
/// </summary>
/// <remarks>
/// Surfaced as HTTP 409 with a stable <see cref="Code"/>, the same shape as
/// <see cref="BelowCostSaleBlockedException"/>: a deliberate refusal on a stale picker, not a validation
/// error or an unexpected fault. The picker already hides these terms, so reaching this means the ladder
/// changed or stock moved between the quote and the POST — the customer is told rather than silently
/// downgraded.
/// </remarks>
public sealed class TermStockUnavailableException : Exception
{
    public const string Code = "term_stock_unavailable";

    public TermStockUnavailableException(string fuelTypeId)
        : base($"The selected term for fuel {fuelTypeId} is not backed by stock and cannot be sold.")
    {
    }
}
