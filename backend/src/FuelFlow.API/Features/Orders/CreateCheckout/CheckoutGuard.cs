using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Orders.CreateCheckout;

/// <summary>
/// The checkout's guard: one basket, one order awaiting payment, one invoice.
/// </summary>
/// <remarks>
/// <para>
/// This is where the double tap is decided. The rule is simple and it is enforced by the database:
/// while an order for a basket is awaiting payment, that basket is taken, and a second request for
/// it is the SAME request. The column <c>orders.checkout_key</c> carries the basket and a unique
/// index over the orders that await payment refuses the second one.
/// </para>
/// <para>
/// What the index alone does not do is stop the second request from minting an invoice on the way
/// to being refused. So the order is written BEFORE the invoice is asked for: the request that
/// loses the race is refused by the database at that moment, and it never reaches the acquirer. The
/// request that wins writes its order, asks for the invoice, and writes the invoice onto it.
/// </para>
/// <para>
/// The one thing that is NOT unique is the order's own key. A refund is matched back to its order
/// through the external reference the acquirer is given, so two orders may not share one - which is
/// why the basket's identity lives in its own column and not in the order's key.
/// </para>
/// </remarks>
public static class CheckoutGuard
{
    /// <summary>
    /// How long a pending order holds its basket. A retry and a double tap land within seconds of
    /// each other; a customer who wants to buy the same fuel again an hour later is a different
    /// purchase, and the hour is where the old behaviour drew that line.
    /// </summary>
    public static readonly TimeSpan ReuseWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// How long a request waits for the invoice of the attempt that holds its basket. The winner is
    /// already committed to the acquirer by then, so the invoice is seconds away - and the acquirer's
    /// own timeout is what bounds the wait, so a hung acquirer cannot hold a request open for ever.
    /// </summary>
    public static readonly TimeSpan InvoiceWait = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The order that holds this basket and still awaits payment, if there is one.
    /// </summary>
    /// <remarks>
    /// Read through the index, by equality: the basket is a column, so the database serves this
    /// with the index and not with a scan. It used to be a prefix match on a key that carried a
    /// random suffix, which no b-tree can serve.
    /// </remarks>
    public static Task<Order?> FindPendingAsync(
        ApplicationDbContext context,
        string checkoutKey,
        CancellationToken cancellationToken)
        => context.Orders
            .FirstOrDefaultAsync(
                o => o.CheckoutKey == checkoutKey && o.Status == OrderStatus.PendingPayment,
                cancellationToken);

    /// <summary>
    /// Whether this pending order has been waiting long enough that it is no longer the current
    /// attempt - a checkout the customer walked away from. Such an order gives its basket back, and
    /// the next request for that basket is a new purchase.
    /// </summary>
    public static bool IsStale(Order order, DateTime nowUtc)
        => order.CreatedAtUtc <= nowUtc - ReuseWindow;

    /// <summary>
    /// Gives the basket back to the customer.
    /// </summary>
    /// <remarks>
    /// The order itself is not touched - it keeps its price, its status and its invoice, and a
    /// customer who pays that invoice late still gets what they paid for. Only the claim on the
    /// basket is released, so that a new attempt for the same basket can be written.
    /// <para>
    /// It is also what an attempt that failed to get an invoice does on its way out: the attempt is
    /// over, and an attempt that is over must not hold the basket against the customer's next try.
    /// </para>
    /// </remarks>
    public static void Release(Order order) => order.CheckoutKey = null;

    /// <summary>
    /// Whether this failure is the database refusing a second order for the same basket.
    /// </summary>
    /// <remarks>
    /// A refusal that names itself is an answer; a refusal that arrives as an unhandled exception
    /// is a 500 in front of a customer who has already been charged once. The loser of the race
    /// reads the winner's order and hands it back instead.
    /// <para>
    /// The unique-violation state is the whole test, not the constraint's name: an order insert
    /// has exactly one unique index it can hit, the basket's, because the order's own key carries
    /// a random suffix, its identifier is fresh and its invoice is not there yet.
    /// </para>
    /// </remarks>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }

        return false;
    }

    /// <summary>
    /// Waits for the attempt that holds the basket to carry its invoice, and returns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attempt writes its order first and asks for the invoice afterwards, so for a moment the
    /// order exists without one. The request that finds it waits for the invoice rather than asking
    /// the acquirer for a second one, which is the whole point: two invoices for one basket is the
    /// double charge this guard exists to prevent.
    /// </para>
    /// <para>
    /// It gives up in two cases. The attempt may have failed - the acquirer refused, or was
    /// unreachable - and a failed attempt gives the basket back, which the wait sees. And the wait
    /// is bounded, so a request never hangs: what the customer is told is that the order exists.
    /// </para>
    /// </remarks>
    public static async Task<Order?> WaitForInvoiceAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + InvoiceWait;

        while (true)
        {
            var order = await context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
                return null;

            if (!string.IsNullOrEmpty(order.MonobankPaymentUrl))
                return order;

            // The attempt is over: it failed, and it gave the basket back. There is no invoice
            // coming, and the basket is free for a new attempt.
            if (order.CheckoutKey is null)
                return null;

            if (DateTime.UtcNow >= deadline)
                return null;

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
