namespace FuelFlow.Features.Orders;

using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// What each checkout refusal costs the customer over HTTP: the status, and the code the app keys its
/// own sentence on.
/// </summary>
/// <remarks>
/// <para>
/// One table for all three taps that can create an order — <c>/api/purchases</c>, its <c>bulk</c>
/// sibling and <c>renew</c> — because a refusal wired onto only two of the three endpoints is a
/// customer who gets a 500 for a refusal that has a name. That is not hypothetical: the below-cost
/// and term refusals were reachable from the cart endpoint alone, and the payment-in-progress one
/// from two of the three. <c>renew</c> consults this after its own family, because a renewal carries
/// a quote error the checkout has no shape for; and it never arrives with the promotion's refusal,
/// because a renewal is written on the payment route whoever asks for it.
/// </para>
/// <para>
/// A refusal returns; anything else returns <c>null</c> and the caller logs it as the unexpected
/// failure it is. The mapping never swallows: an exception with no row here takes the same path it
/// took before this file existed.
/// </para>
/// </remarks>
public static class CheckoutRefusals
{
    public static IActionResult? TryMap(Exception exception) => exception switch
    {
        // The fuel is in stock and nobody has recorded what we paid for it, so the price cannot be
        // checked against a cost. A distinct code, because "we do not know" is not "below cost" - the
        // first needs a cost recorded, the second a price raised.
        UnknownCostSaleBlockedException ex => ApiError.Conflict(ex.Message, UnknownCostSaleBlockedException.Code),
        BelowCostSaleBlockedException ex => ApiError.Conflict(ex.Message, BelowCostSaleBlockedException.Code),

        // The term was sold, and the stock behind it is not there in the litres that term promised.
        TermStockUnavailableException ex => ApiError.Conflict(ex.Message, TermStockUnavailableException.Code),
        PackageOutOfStockException ex => ApiError.Conflict(ex.Message, PackageOutOfStockException.Code),

        // The customer pressed the promotional button on an account a manager never opened to the
        // offer. Forbidden, not Conflict: nothing about the basket has to change for this to work,
        // a person's decision does (planning #889).
        PromoAccessException ex => ApiError.Forbidden(ex.Message, PromoAccessException.Code),

        // The offer is open and the money is not enough. Conflict, because the account has to change
        // for the same tap to work — the figure is the whole answer, so it travels as `data` and the
        // phone builds its own sentence instead of parsing ours (planning #889, item 5).
        PromoBalanceShortException ex => ApiError.Conflict(
            ex.Message,
            PromoBalanceShortException.Code,
            data: new Dictionary<string, object?>
            {
                ["shortfallUah"] = ex.ShortfallUah,
                ["heldUah"] = ex.HeldUah,
                ["owedUah"] = ex.OwedUah,
            }),

        // The other button was pressed while this cart's order still had money in motion. The
        // customer's way out is not a retry: that order has to be paid or deleted first.
        CheckoutAlreadyInPaymentException ex => ApiError.Conflict(ex.Message, CheckoutAlreadyInPaymentException.Code),

        AccountInactiveException ex => ApiError.Forbidden(ex.Message, AccountInactiveException.Code),

        // 503 and not 4xx: the platform wrote the order, the acquirer refused to open a payment page,
        // and nothing was charged. "Try again in a few minutes" is the truth the customer can act on.
        // The sentence here is the platform's own, not ex.Message: what the bank answered is for the
        // log an operator reads, and it has never been a sentence a phone should show.
        MonobankInvoiceRejectedException => ApiError.ServiceUnavailable(
            "The payment service is not accepting payments right now. Nothing has been charged; please try again in a few minutes.",
            MonobankInvoiceRejectedException.Code),

        _ => null
    };
}
