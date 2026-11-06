namespace FuelFlow.Features.Orders.CreateCheckout;

using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// What a checkout does about settlement routes: which one the basket already carries, and what may
/// be done about a request that wants a different one.
/// </summary>
/// <remarks>
/// <para>
/// The owner's ruling of 2026-11-06 (planning #889) gives an account that has been opened to the
/// offer a second button beside the payment one, and the two buttons lead to different money: one to
/// the acquirer, one to the settlement channel's held balance. Every rule in this file follows from
/// that, and the awkward one is what happens when a customer presses both.
/// </para>
/// <para>
/// The basket is still one basket. <see cref="CheckoutGuard"/>'s index refuses two orders awaiting
/// payment for the same cart, and that is right for a reason the route makes sharper than it was: two
/// live orders for one cart is not two attempts, it is two ways to pay for the same fuel. So a new
/// tap either continues the order that holds the basket, takes it over, or is refused - and the
/// choice between those is decided by whether money could already be moving on the order that is
/// there.
/// </para>
/// </remarks>
public static class CheckoutRoutes
{
    /// <summary>Where the basket stands once this request's route has been put next to it.</summary>
    public enum BasketState
    {
        /// <summary>The basket is this request's to take: the old attempt was given up or walked away from.</summary>
        Free,

        /// <summary>The order that holds the basket is this request's own answer. Give it back, write nothing.</summary>
        Reuse,

        /// <summary>
        /// The basket is held by an order that money may already be moving on, and that order is not
        /// the one this request is asking for. Refuse rather than choose for the customer.
        /// </summary>
        InFlight,
    }

    /// <summary>
    /// The route an order was written on. An order from before the column existed was a checkout, and
    /// a checkout that predates the second button had one thing it could ask for.
    /// </summary>
    public static SettlementRoute Of(Order order) => order.SettlementRoute ?? SettlementRoute.Acquirer;

    /// <summary>
    /// The route a request asks for, which is the payment one whenever the client did not say.
    /// </summary>
    /// <remarks>
    /// A missing field is not a missing decision: every app version in the field today sends a
    /// checkout that means the bank, and the published bundles cannot be made to say otherwise. The
    /// default is therefore the historical answer, not a convenient one - and unlike a default that
    /// invented a route, it asks somebody to pay before anything is fulfilled.
    /// </remarks>
    public static SettlementRoute Resolve(SettlementRoute? requested)
        => requested ?? SettlementRoute.Acquirer;

    /// <summary>
    /// Refuse a request on the declared route from an account nobody opened to the offer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route arrives as one field of a body the customer controls, so the second button exists on
    /// the server whether or not it exists on the phone. The grant is
    /// <c>users.promo_access_granted_at_utc</c>, and only a Manager or above may write it (planning
    /// #889, and the capability that enforces it is <c>AuthPolicies.PromoAccess</c>) — which is what
    /// makes a non-null value a decision by a named person rather than a preference of the account.
    /// </para>
    /// <para>
    /// The payment route is never refused here. It is the one every account has always had, and
    /// blocking it on a promotion's rules would turn a gate on the offer into a way to stop a sale.
    /// </para>
    /// </remarks>
    public static void RequirePromoAccess(SettlementRoute route, User user)
    {
        if (route == SettlementRoute.Declared && user.PromoAccessGrantedAtUtc is null)
            throw new PromoAccessException();
    }

    /// <summary>
    /// Decide what the pending order that holds this basket means for a request on
    /// <paramref name="requested"/>, and carry out the part that is a write (giving a stale attempt's
    /// basket back, giving up a declared order nobody has paid).
    /// </summary>
    /// <remarks>
    /// The whole of it is one question, asked in a careful order: <b>could the order that is already
    /// there have money moving on it?</b> If it could, nothing here touches it, because a customer who
    /// has been to the bank's payment page, or whose declaration has already been named on the order,
    /// is mid-transaction - and the two ways to end that transaction (cancel it, or pay it and buy
    /// again) are both louder than a checkout that silently picks one. If it could not, the previous
    /// attempt is just a row in the way, and taking it out is what the customer's new tap means.
    /// </remarks>
    public static async Task<(BasketState State, Order? Order)> SettleBasketAsync(
        ApplicationDbContext context,
        Order pending,
        SettlementRoute requested,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // A checkout the customer walked away from. The order keeps everything it was - its price, its
        // status, its invoice, and a customer who pays that invoice late still gets what they paid for
        // - and gives the basket back, so the next request for it is a new purchase. This is the same
        // answer on both routes, and it is asked first because it is the one case where the old
        // attempt is known to be over.
        if (CheckoutGuard.IsStale(pending, nowUtc))
        {
            if (await WritableAsync(context, pending, cancellationToken) is { } stale)
                CheckoutGuard.Release(stale);

            await context.SaveChangesAsync(cancellationToken);
            return (BasketState.Free, null);
        }

        var held = Of(pending);

        // A different route on the same basket: the customer changed their mind about how to pay.
        if (held != requested)
            return held == SettlementRoute.Declared
                ? await GiveUpAsync(context, pending, nowUtc, cancellationToken)
                : (BasketState.InFlight, pending);

        // The same route, and on the declared one the order is the whole answer. Waiting is what an
        // acquirer retry does for the invoice of the attempt that is still running; a declared order
        // never gets an invoice, so the wait would only end in a timeout that looks like a failure.
        if (held == SettlementRoute.Declared)
            return (BasketState.Reuse, pending);

        var withInvoice = await CheckoutGuard.WaitForInvoiceAsync(context, pending.Id, cancellationToken);

        if (withInvoice != null)
            return (BasketState.Reuse, withInvoice);

        // The attempt is over and it produced no invoice: the acquirer refused it, or the request that
        // owned it died. The basket is free, so this request makes a new attempt.
        if (await WritableAsync(context, pending, cancellationToken) is { } refused)
            CheckoutGuard.Release(refused);

        await context.SaveChangesAsync(cancellationToken);
        return (BasketState.Free, null);
    }

    /// <summary>
    /// End a pending declared order so that the basket can be re-attempted on another route, and say
    /// whether that was allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cancelling is safe here for a reason specific to this route: the acquirer was never asked for
    /// an invoice, so there is no payment page open anywhere and nothing a customer could pay a
    /// moment after we called it off. An <see cref="SettlementRoute.Acquirer"/> order does not have
    /// that guarantee, which is why the caller never brings one here.
    /// </para>
    /// <para>
    /// The one thing that can still have arrived is money named on the order by the settlement
    /// channel - a declaration that came in for it while it waited. That money is real, the finance
    /// team put it there by hand, and an order carrying it is not ours to dissolve as a side effect of
    /// a tap; the refusal sends the customer to somebody who can see the money.
    /// </para>
    /// </remarks>
    private static async Task<(BasketState, Order?)> GiveUpAsync(
        ApplicationDbContext context,
        Order pending,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var moneyIsNamedOnIt = await context.Payments
            .AsNoTracking()
            .AnyAsync(p => p.OrderId == pending.Id, cancellationToken);

        if (moneyIsNamedOnIt)
            return (BasketState.InFlight, pending);

        // PendingPayment -> Cancelled is the only move the state machine allows from here, and the row
        // is kept: an order that was placed and given up is part of the account's history.
        if (await WritableAsync(context, pending, cancellationToken) is not { } toGiveUp)
            return (BasketState.Free, null);

        toGiveUp.Status = OrderStatus.Cancelled;
        toGiveUp.CheckoutKey = null;
        toGiveUp.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
        return (BasketState.Free, null);
    }

    /// <summary>
    /// The instance of this order that this file may write, or nothing when the row is gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller hands an order over to decide with, and this re-read is what changes it. That split
    /// is deliberate rather than tidy: the order arrives from a query in whichever handler called here,
    /// and whether that query is tracked is not something this file can see. An untracked entity is
    /// still a correct value to decide on and a silent no-op to write to — the trap
    /// <c>NoTrackingMutationGuardTests</c> exists to catch, which it can only catch per file, and this
    /// file is the one that writes. Re-reading with <c>AsTracking()</c> makes the write hold whatever
    /// the caller did, and costs one read on the primary key on the three branches that were writing
    /// anyway. When the row is already tracked this returns the same instance the caller holds, so the
    /// two never disagree.
    /// </para>
    /// <para>
    /// Gone is reachable, and only on the last branch: that branch waits for the invoice of the attempt
    /// for up to <see cref="CheckoutGuard.InvoiceWait"/>, and in that window the expiry job or the
    /// customer's own delete can take the order out. Its basket went with it, so the answer is the same
    /// one the row being here would have given.
    /// </para>
    /// </remarks>
    private static Task<Order?> WritableAsync(
        ApplicationDbContext context,
        Order pending,
        CancellationToken cancellationToken)
        => context.Orders
            .AsTracking()
            .FirstOrDefaultAsync(o => o.Id == pending.Id, cancellationToken);
}
