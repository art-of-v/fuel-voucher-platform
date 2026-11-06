using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SharedModels;

/// <summary>
/// The money question about an order: has its money arrived?
/// </summary>
/// <remarks>
/// <para>
/// One place answers it, because the answer decides whether fuel is handed over. The check is on
/// the <b>payment row</b> — the money record — and not on the order's own status: the order's
/// status is what a bug (or an operator) writes, and the payment row is what the acquirer says.
/// A capture in state <see cref="PaymentStatus.Succeeded"/> is the only state that means the
/// money is ours: <c>Pending</c> means an invoice exists and nobody paid it, and
/// <c>Failed</c>/<c>Cancelled</c>/<c>Expired</c> mean it will never be.
/// </para>
/// <para>
/// The acquirer's webhook applies that status to the payment row <i>after</i> it advances the
/// order, so for a few milliseconds an order can sit in a fulfilment-eligible state while its
/// payment still says <c>Pending</c>. A fulfilment that reads the row in that window refuses the
/// order and the sweep retries it — the failure mode is "no vouchers yet", never "vouchers for
/// nothing".
/// </para>
/// <para>
/// <b>Two sources, because money reaches an order two ways.</b> A whole capture naming the order —
/// which is what an acquirer's invoice does — and a draw made on held money, which names no order
/// and is what one declared transfer covering two sales leaves behind. The two sets are disjoint by
/// construction, and the database keeps them disjoint: a payment either carries an order or is drawn
/// on, never both. Every question in this file is therefore a sum over both, and a caller that reads
/// one of them alone is reading half the money.
/// </para>
/// </remarks>
public static class OrderPayment
{
    /// <summary>
    /// Whether any money has been received for the order, at all.
    /// </summary>
    /// <remarks>
    /// This is the weaker of the two questions in this file and it is not the settle's: it asks whether
    /// any money came, not whether what came covers the price. A caller deciding whether an order may be
    /// <i>paid for</i> wants <see cref="MoneyOnItPaysForItAsync"/>. This one is what the fulfilment job
    /// asks, and it stays the weaker read on purpose: production's delivered orders include two with no
    /// capture row at all — the zero-price ones a company received as vouchers — and a money-total gate
    /// there would hold back fuel for a delivery that was never a sale.
    /// <para>
    /// An order paid from the balance has no capture naming it, so the draws are asked as well. Without
    /// this the fulfilment job would refuse, and then retry forever, an order the settle had already
    /// paid for and booked.
    /// </para>
    /// </remarks>
    public static async Task<bool> HasCapturedPaymentAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken = default)
        => await context.Payments
            .AsNoTracking()
            .AnyAsync(
                p => p.OrderId == orderId
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded,
                cancellationToken)
           || await context.PaymentAllocations
            .AsNoTracking()
            .AnyAsync(
                a => a.OrderId == orderId
                     && a.Payment.Kind == PaymentKind.Capture
                     && a.Payment.Status == PaymentStatus.Succeeded,
                cancellationToken);

    /// <summary>
    /// How much of the order's price has been answered for, in UAH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both sources, and one number: the whole captures naming the order plus the draws made on held
    /// money for it. Split out of <see cref="MoneyOnItPaysForItAsync"/> because the checkout that pays
    /// from the balance has to know not merely whether the money is enough but by how much it is not —
    /// and the only figure an operator or a customer can act on is the shortfall.
    /// </para>
    /// <para>
    /// A draw is counted only while the payment behind it is still a succeeded capture, exactly like an
    /// attached one. Money the acquirer reversed has been given back, and an allocation pointing at it
    /// would be a claim on a banknote the customer already has.
    /// </para>
    /// </remarks>
    public static async Task<decimal> MoneyOnItAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        // Nullable projections: SumAsync over an empty sequence answers NULL, and the cast that would
        // turn that into a number is the one this file has always had to write by hand.
        var attached = await context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded)
            .Select(p => (decimal?)p.AmountUah)
            .SumAsync(cancellationToken);

        var drawn = await context.PaymentAllocations
            .AsNoTracking()
            .Where(a => a.OrderId == orderId
                     && a.Payment.Kind == PaymentKind.Capture
                     && a.Payment.Status == PaymentStatus.Succeeded)
            .Select(a => (decimal?)a.AmountUah)
            .SumAsync(cancellationToken);

        return (attached ?? 0m) + (drawn ?? 0m);
    }

    /// <summary>
    /// Whether the money already on the order pays for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question every settle has to answer before it moves an order, whoever is asking. It is
    /// deliberately provider-agnostic: an acquirer's <c>success</c> and a declared settlement are two
    /// different accounts of HOW the money came to be known, and this reads the one fact they both
    /// end in — a succeeded capture naming the order, or a draw naming it from a capture that does not.
    /// </para>
    /// <para>
    /// A sum rather than an equality, because a customer can pay one order in two real transfers;
    /// <c>&gt;=</c> rather than <c>==</c>, because an over-payment is a customer's mistake and does not
    /// undo the fact that they paid. Anything short of the price is not settled: the money is real and
    /// belongs on the order, and the order still owes for what is missing.
    /// </para>
    /// <para>
    /// <c>IncludingSoftDeleted</c> on the price is not a loophole. A hidden order whose money arrived is
    /// still a sale the customer paid for, and the deletion filter is a listing concern — it is what
    /// keeps a cancelled-and-abandoned order out of the customer's screen, not a rule about money.
    /// </para>
    /// </remarks>
    public static async Task<bool> MoneyOnItPaysForItAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        // Nullable on purpose: a non-nullable projection answers 0 for an order that does not exist,
        // and 0 >= 0 would settle a phantom.
        var price = await context.Orders
            .AsNoTracking()
            .IncludingSoftDeleted()
            .Where(o => o.Id == orderId)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefaultAsync(cancellationToken);

        return price is { } owed
            && await MoneyOnItAsync(context, orderId, cancellationToken) >= owed;
    }
}
