using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Finds the money the customer has already paid for an order that does not exist yet, and pays for
/// the order that now does.
/// </summary>
/// <remarks>
/// <para>
/// The match is deliberately narrow, and it is the same rule the settlement channel applies from the
/// other side: <b>one</b> payment, in <see cref="PaymentStatus.Succeeded"/>, a capture, naming this
/// customer, naming no order, for <b>exactly</b> this price. Anything looser is a guess about which
/// money belongs to which sale, and a guess in this direction is worse than leaving the money where
/// the operator can see it — it hands over fuel against someone else's transfer, or against half of
/// this customer's.
/// </para>
/// <para>
/// A refund is excluded by <see cref="PaymentKind"/>, and a money row that already names an order is
/// excluded by <c>OrderId == null</c>: this is about held money, not about re-spending a payment that
/// has been used. An acquirer capture that never got applied is excluded too, because it is booked
/// with no customer at all, and a payment we cannot attribute to this person is not this person's
/// money.
/// </para>
/// <para>
/// The attach and the settle are one unit, in one transaction, because a payment moved onto an order
/// that then did not settle is a half-state, and a settled order with no receipt in the books is the
/// one this issue was opened about. <see cref="TrySettlePaidOrderCommandHandler"/> holds the rules;
/// this class only decides whether it is worth asking.
/// </para>
/// </remarks>
public sealed class SettleFromHeldMoneyCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly TrySettlePaidOrderCommandHandler _settle;
    private readonly ILogger<SettleFromHeldMoneyCommandHandler> _logger;

    public SettleFromHeldMoneyCommandHandler(
        ApplicationDbContext context,
        TrySettlePaidOrderCommandHandler settle,
        ILogger<SettleFromHeldMoneyCommandHandler> logger)
    {
        _context = context;
        _settle = settle;
        _logger = logger;
    }

    public async Task<SettleFromHeldMoneyResult> HandleAsync(
        SettleFromHeldMoneyCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == command.OrderId)
            .Select(o => new { o.Id, o.UserId, o.Price, o.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return SettleFromHeldMoneyResult.Declined(
                HeldMoneyOutcome.OrderNotWaiting, "No such order.");
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            // Only an order awaiting its payment is paid from held money. A fulfilled, cancelled or
            // already-settled order is somebody else's decision by now.
            return SettleFromHeldMoneyResult.Declined(
                HeldMoneyOutcome.OrderNotWaiting,
                $"The order is {order.Status}, which is not waiting for a payment.");
        }

        // Two rows are read, not one, because "more than one" is a different answer from "one" and the
        // caller must not guess which it got.
        var held = await _context.Payments
            .AsNoTracking()
            .Where(p => p.UserId == order.UserId
                     && p.OrderId == null
                     && p.Kind == PaymentKind.Capture
                     && p.Status == PaymentStatus.Succeeded
                     && p.AmountUah == order.Price)
            .OrderBy(p => p.PaidAtUtc)
            .Select(p => new { p.Id, p.Provider, p.AmountUah })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (held.Count == 0)
        {
            return SettleFromHeldMoneyResult.Nothing();
        }

        if (held.Count > 1)
        {
            _logger.LogWarning(
                "HELD MONEY: order {OrderId} for {Amount:0.00} ₴ found {Count} unattached payments of that amount "
                 + "for user {UserId}; leaving the choice to an operator.",
                order.Id, order.Price, held.Count, order.UserId);

            return SettleFromHeldMoneyResult.Declined(
                HeldMoneyOutcome.SeveralToChoose,
                $"There is more than one payment of {order.Price:0.00} ₴ held for this customer, "
                + "so which one pays for this order is not something to guess.");
        }

        var paymentId = held[0].Id;

        return await _context.ExecuteReplaySafeAsync(async ct =>
        {
            // AsTracking, stated rather than inherited: this mutates the row and saves, and under the
            // project's NoTracking default it would be a silent no-op that still reported a settle.
            var payment = await _context.Payments
                .AsTracking()
                .FirstOrDefaultAsync(p => p.Id == paymentId, ct);

            if (payment is null)
            {
                // It was there a moment ago and is not now: another path attached it first. That path
                // owns the settle, and this one has nothing to add.
                return SettleFromHeldMoneyResult.Declined(
                    HeldMoneyOutcome.NothingHeld, "The money was taken by another request.");
            }

            payment.OrderId = order.Id;
            payment.UpdatedAtUtc = SystemClock.UtcNow;

            // Written before the core is asked, and inside the same transaction. The core sums the
            // money the database says is on the order; an attach that only exists in this context's
            // change tracker is not in that sum, and the answer would be "short" for money that is
            // standing right there. Without this line the checkout attached the money, watched the
            // settle refuse it, and carried on to mint an invoice for an order it had just been paid
            // for — which the test that found this proves, rather than asserts about.
            await _context.SaveChangesAsync(ct);

            var settled = await _settle.HandleAsync(
                new TrySettlePaidOrderCommand
                {
                    OrderId = order.Id,
                    Because = FormattableString.Invariant(
                        $"the platform found {payment.AmountUah:0.00} ₴ declared on {payment.Provider} and held for this customer, and put it on order {order.Id}")
                },
                ct);

            if (!settled.Settled)
            {
                // The money stays attached. It is this customer's money for this order whether or not
                // the order could move, and the reason is the operator's to read.
                _logger.LogWarning(
                    "HELD MONEY: attached payment {PaymentId} to order {OrderId} and the settle declined — {Reason}",
                    payment.Id, order.Id, settled.Reason);

                return new SettleFromHeldMoneyResult
                {
                    Outcome = HeldMoneyOutcome.AttachedButDeclined,
                    PaymentId = payment.Id,
                    Reason = settled.Reason
                };
            }

            _logger.LogInformation(
                "HELD MONEY: payment {PaymentId} ({Amount:0.00} ₴, {Provider}) settled order {OrderId} without an invoice.",
                payment.Id, payment.AmountUah, payment.Provider, order.Id);

            return SettleFromHeldMoneyResult.PaidBy(payment.Id);
        }, cancellationToken);
    }
}
