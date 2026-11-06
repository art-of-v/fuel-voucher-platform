namespace FuelFlow.Features.Orders.CreateCheckout;

using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Renewal;
using FuelFlow.Features.Vouchers.Terms;

/// <summary>
/// The term a cart line was bought on, resolved into the discount to apply and the term code to
/// freeze onto the line.
/// </summary>
/// <remarks>
/// Kept apart from the checkout that calls it because it is the part of the bulk path that is not
/// about the order at all: it reads a manager's ladder and the customer's request, and answers a
/// price. The bulk handler is the only caller today, and it has enough to do - the basket, the race,
/// the stock, the cost guards, the acquirer.
/// </remarks>
public static class CheckoutTermResolver
{
    /// <summary>
    /// Resolves the term, returning <c>(0, null)</c> — the voucher's full remaining life at the
    /// undiscounted price — when no term was requested or the feature is off.
    /// </summary>
    /// <remarks>
    /// Fail-safe in the direction that costs nothing: the feature off, an absent term, an unparseable
    /// term, or a tier the manager has not configured all fall back to the full term rather than
    /// rejecting the sale. A customer must never be unable to buy fuel over a settings problem. Stock
    /// sufficiency is a separate gate at fulfilment, where the voucher is actually claimed.
    /// </remarks>
    public static (decimal DiscountPerLiter, string? TermCode) Resolve(
        CheckoutItem item, VoucherTermConfig config, Guid userId, ILogger logger)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(item.TermCode))
            return (0m, null);

        if (!VoucherRenewalTerms.TryFromCode(item.TermCode, out var term))
        {
            logger.LogWarning(
                "Ignoring unknown purchase term {TermCode} for user {UserId}; selling the full term instead",
                item.TermCode, userId);
            return (0m, null);
        }

        var tier = config.Tier(term);
        if (tier is null || !tier.IsOfferable)
        {
            logger.LogWarning(
                "Purchase term {TermCode} is not configured for user {UserId}; selling the full term instead",
                item.TermCode, userId);
            return (0m, null);
        }

        return (tier.DiscountPerLiterUah, term.Code());
    }
}
