namespace FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// How the money for this order is expected to arrive, decided at the moment the order is written
/// and never again. Stored as the enum name, so the database reads <c>Acquirer</c> / <c>Declared</c>
/// verbatim, and <c>NULL</c> for an order nobody paid for by tapping anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the second of the two axes described in <c>docs/ACQUISITION_CHANNELS.md</c>, and it is
/// easy to confuse with the first. The cost axis (<c>fuel_vouchers.acquisition_source</c>) says where
/// a <em>voucher</em> came from. This one says how an <em>order</em> is settled, and the two are
/// independent: a declared-route order can be filled with stock bought directly, and it is — that is
/// every sale made so far.
/// </para>
/// <para>
/// The owner's ruling of 2026-11-06 (planning #889) is what put the choice in the customer's hands:
/// a customer who is given access to the promotional offer gets a second button beside the payment
/// one, and the two buttons lead to different money. Before it, every order in the system was one
/// tap at the bank, so <c>NULL</c> and <c>Acquirer</c> describe the same history from two directions —
/// the first is what the row says about itself, the second is what the customer chose.
/// </para>
/// <para>
/// The name of the value is deliberately not the name of the offer. Nothing here says <i>promo</i>:
/// the column is about the settlement, and the reason a customer is on that settlement is somebody
/// else's business (see the neutrality rule in <c>ACQUISITION_CHANNELS.md</c> §4).
/// </para>
/// </remarks>
public enum SettlementRoute
{
    /// <summary>
    /// The customer pays now, through the acquirer: the checkout asks Monobank for an invoice and the
    /// payment page is what the customer is given. The only route that has ever existed in
    /// production, and the one every order with an invoice behind it carries.
    /// </summary>
    Acquirer = 0,

    /// <summary>
    /// The order spends money the platform already holds, or is waiting for. The acquirer is never
    /// asked: no invoice, no payment page, no merchant — which is what
    /// <c>ck_order_declared_route_asks_no_acquirer</c> holds the row to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Declared" is the word <c>payments.provider</c> already uses for money a person states arrived
    /// rather than money an acquirer reported, and the order says the same thing about itself: this
    /// order is paid out of that. The value is not a claim that the money is there — an order can be
    /// written before the declaration comes in, which is the ordinary case — only that the money will
    /// come that way and not from a card.
    /// </para>
    /// <para>
    /// Only an account that a manager has opened to the offer may have one (planning #889), and the
    /// grant is <c>users.promo_access_granted_at_utc</c>.
    /// </para>
    /// </remarks>
    Declared = 1
}
