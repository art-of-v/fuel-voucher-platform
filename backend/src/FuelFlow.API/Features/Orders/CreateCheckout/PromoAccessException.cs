namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// A checkout asked for the declared settlement route on an account that nobody opened to it.
/// Surfaces as HTTP 403 with a stable <see cref="Code"/> — the same shape as the other checkout
/// refusals, because the mobile client keys its own copy on the code rather than on this sentence.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the gate the owner's ruling of 2026-11-06 (planning #889) asks for: a
/// customer cannot reach the promotional offer by themselves, only a Manager or above can open an
/// account to it. The button is one field in a request body the customer controls, so the rule that
/// decides who may press it is here and nowhere else — an unchecked declared route is a way to be
/// served fuel with the acquirer never seeing the sale.
/// </para>
/// <para>
/// It is not the same as a refusal for lack of money. Money is checked when the order is settled
/// (planning #889 slice 3); this says the account is not on the list, whatever it holds.
/// </para>
/// </remarks>
public sealed class PromoAccessException : Exception
{
    public const string Code = "promo_access_not_granted";

    public PromoAccessException()
        : base("This offer is open to accounts our managers have enrolled. Pay by card, or ask us to enable the offer for your account.")
    {
    }
}
