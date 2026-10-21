using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
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
}
