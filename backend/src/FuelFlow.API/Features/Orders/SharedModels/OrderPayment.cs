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
/// </remarks>
public static class OrderPayment
{
    /// <summary>
    /// Whether a capture payment for the order has been received.
    /// </summary>
    /// <remarks>
    /// This is the weaker of the two questions in this file and it is not the settle's: it asks whether
    /// any money came, not whether what came covers the price. A caller deciding whether an order may be
    /// <i>paid for</i> wants <see cref="MoneyOnItPaysForItAsync"/>. This one is what the fulfilment job
    /// asks, and it stays the weaker read on purpose: production's delivered orders include two with no
    /// capture row at all — the zero-price ones a company received as vouchers — and a money-total gate
    /// there would hold back fuel for a delivery that was never a sale.
    /// </remarks>
    public static Task<bool> HasCapturedPaymentAsync(
        ApplicationDbContext context,
        Guid orderId,
        CancellationToken cancellationToken = default)
        => context.Payments
            .AsNoTracking()
            .AnyAsync(
                p => p.OrderId == orderId
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded,
                cancellationToken);

    /// <summary>
    /// Whether the money already attached to the order pays for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question every settle has to answer before it moves an order, whoever is asking. It is
    /// deliberately provider-agnostic: an acquirer's <c>success</c> and a declared settlement are two
    /// different accounts of HOW the money came to be known, and this reads the one fact they both
    /// end in — a succeeded capture naming the order.
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
        var paid = await context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded)
            .Select(p => (decimal?)p.AmountUah)
            .SumAsync(cancellationToken);

        // Nullable on purpose: a non-nullable projection answers 0 for an order that does not exist,
        // and 0 >= 0 would settle a phantom.
        var price = await context.Orders
            .AsNoTracking()
            .IncludingSoftDeleted()
            .Where(o => o.Id == orderId)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefaultAsync(cancellationToken);

        return price is { } owed && (paid ?? 0m) >= owed;
    }
}
