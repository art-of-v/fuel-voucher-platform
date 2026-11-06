using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Finds the money the customer has already paid for an order that does not exist yet, and spends
/// enough of it to pay for the order that now does.
/// </summary>
/// <remarks>
/// <para>
/// <b>The customer's held money is a balance, and a balance is spent, not matched.</b> This handler
/// used to ask a different question: is there exactly one unattached payment for exactly this price,
/// and if there are two of them, which one is the customer's money answering for? That was the right
/// shape while one transfer was one sale, and it stopped being the right shape the moment finance
/// declared 1 000,00 ₴ for a customer who then bought twice. The ruling is that one declared
/// payment may be cut across several orders, and so what an order needs is not a matching transfer but
/// the part of an account: <see cref="PaymentBalance"/> says what is free, and the draw it plans is
/// written to <see cref="PaymentAllocation"/> rows rather than to <c>payments.order_id</c>.
/// </para>
/// <para>
/// <b>What it refuses.</b> Nothing held and not enough held are separate answers, because they want
/// separate actions — an order with no money against it can wait for a transfer that has not arrived,
/// and an order short by 200,00 ₴ is a decision someone has to make. It draws nothing when it is
/// short: a partial draw would spend the customer's whole balance on an order that still cannot be
/// fulfilled, and then the second order — the one the money was actually enough for — finds an empty
/// account. That is a worse place to bring an operator than a refusal that names the figure.
/// </para>
/// <para>
/// <b>The draws and the settle are one unit.</b> A draw the order did not use is not a record of
/// anything, so when the core declines the rows come back out before the transaction closes: the
/// money returns to the balance and the refusal says why. <see cref="TrySettlePaidOrderCommandHandler"/>
/// holds the rules about what may settle; this class decides only whether the balance is worth asking.
/// </para>
/// <para>
/// The read of the money happens before the transaction and the write inside it, which leaves the
/// window the database closes rather than this code: two requests drawing on one transfer both plan
/// legally and the second insert is refused by <c>assert_payment_draws_on_held_money</c>, which takes
/// the lock on the payment before it sums. That refusal is not an error to report — the account has
/// genuinely changed under this request, so the answer is to read it again and say what it holds now.
/// A customer whose balance was spent a second earlier is short, and a checkout that is short asks the
/// acquirer for money instead of settling on money that is no longer there.
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

        // The whole rule of what counts as paid for, from the one place that defines it, so that this
        // and the settle core cannot disagree about an order part-paid by an earlier transfer.
        var owed = order.Price - await OrderPayment.MoneyOnItAsync(_context, order.Id, cancellationToken);

        if (owed <= 0m)
        {
            // Nothing to draw and the order is covered: the money is already on it, and the only thing
            // missing is the settle. Asking the core is what closes the case of money placed by hand
            // while the order went on waiting for an invoice nobody was going to pay.
            return await _context.ExecuteReplaySafeAsync(async ct =>
            {
                var settled = await _settle.HandleAsync(
                    new TrySettlePaidOrderCommand
                    {
                        OrderId = order.Id,
                        Because = FormattableString.Invariant(
                            $"the {order.Price:0.00} ₴ that pays for order {order.Id} was already on it")
                    },
                    ct);

                return settled.Settled
                    ? SettleFromHeldMoneyResult.PaidBy([])
                    : SettleFromHeldMoneyResult.Declined(
                        HeldMoneyOutcome.OrderNotWaiting, CoreReason(settled));
            }, cancellationToken);
        }

        var held = await PaymentBalance.AvailableAsync(_context, order.UserId, cancellationToken);

        if (held.Count == 0)
        {
            return SettleFromHeldMoneyResult.Nothing();
        }

        var plan = PaymentBalance.DrawAgainst(held, owed);

        if (!plan.Covers)
        {
            var free = held.Sum(h => h.RemainingUah);

            _logger.LogInformation(
                "HELD MONEY: order {OrderId} owes {Owed:0.00} ₴ and the account holds {Held:0.00} ₴, "
                 + "so it is {Short:0.00} ₴ short and nothing was drawn.",
                order.Id, owed, free, plan.ShortfallUah);

            return SettleFromHeldMoneyResult.Short(plan.ShortfallUah, free, owed);
        }

        // Built before the transaction and written inside it, because the refusal below has to take the
        // rows back out of the change tracker after the database has already rolled them back.
        var drawn = plan.Draws.Select(d => new PaymentAllocation
        {
            Id = Guid.CreateVersion7(),
            PaymentId = d.PaymentId,
            OrderId = order.Id,
            AmountUah = d.AmountUah,
            CreatedAtUtc = SystemClock.UtcNow
        }).ToList();

        try
        {
            return await _context.ExecuteReplaySafeAsync(async ct =>
            {
                _context.PaymentAllocations.AddRange(drawn);

                // Written before the core is asked, and inside the same transaction. The core sums the
                // money the database says is on the order; a draw that only exists in this context's
                // change tracker is not in that sum, and the answer would be "short" for money standing
                // right there. Without this line the checkout planned the draw, watched the settle refuse
                // it, and carried on to mint an invoice for an order it had just been paid for.
                await _context.SaveChangesAsync(ct);

                var settled = await _settle.HandleAsync(
                    new TrySettlePaidOrderCommand
                    {
                        OrderId = order.Id,
                        Because = FormattableString.Invariant(
                            $"the platform drew {owed:0.00} ₴ from {drawn.Count} held payment(s) to pay for order {order.Id}")
                    },
                    ct);

                if (!settled.Settled)
                {
                    // Backed out inside the transaction that wrote them, so no committed draw is ever
                    // revised or deleted: the balance is exactly what it was, and the order keeps the
                    // reason the core gave for not moving.
                    _context.PaymentAllocations.RemoveRange(drawn);
                    await _context.SaveChangesAsync(ct);

                    _logger.LogWarning(
                        "HELD MONEY: drew {Amount:0.00} ₴ for order {OrderId} and the settle declined, so the draw "
                         + "goes back to the balance - {Reason}",
                        owed, order.Id, settled.Reason);

                    return SettleFromHeldMoneyResult.Declined(HeldMoneyOutcome.DrawRefused, CoreReason(settled));
                }

                _logger.LogInformation(
                    "HELD MONEY: {Draws} draw(s) totalling {Amount:0.00} ₴ settled order {OrderId} without an invoice.",
                    drawn.Count, owed, order.Id);

                return SettleFromHeldMoneyResult.PaidBy(drawn.Select(d => d.PaymentId).ToList());
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDrawRefusedByTheDatabase(ex))
        {
            // These rows are the only thing this context put into the transaction the database refused,
            // and they went down with it. Detaching them is what keeps the caller's next save - the
            // invoice it is about to ask the acquirer for - from writing a refused draw a second time.
            _context.PaymentAllocations.RemoveRange(drawn);

            // Re-read rather than quote the plan: the refusal means another request spent the money in
            // the meantime and committed, and the figures anyone is given have to be the account's, not
            // the ones this request guessed a moment ago.
            var still = await PaymentBalance.AvailableAsync(_context, order.UserId, cancellationToken);
            var stillOwed = order.Price - await OrderPayment.MoneyOnItAsync(_context, order.Id, cancellationToken);
            var restated = PaymentBalance.DrawAgainst(still, stillOwed);
            var stillFree = still.Sum(h => h.RemainingUah);

            _logger.LogWarning(
                "HELD MONEY: the database refused order {OrderId}'s planned draw of {Owed:0.00} ₴ because another "
                 + "request spent the balance first; the account now holds {Held:0.00} ₴ and is {Short:0.00} ₴ short",
                order.Id, owed, stillFree, restated.ShortfallUah);

            return SettleFromHeldMoneyResult.Short(restated.ShortfallUah, stillFree, stillOwed);
        }
    }

    /// <summary>
    /// Whether this failure is the database refusing the draw, rather than something wrong with the row.
    /// </summary>
    /// <remarks>
    /// <c>RAISE EXCEPTION</c> with no <c>ERRCODE</c> is <c>P0001</c>, which is what both money guards on
    /// this table raise - see the migration that writes them. A malformed row fails on a check
    /// (<c>23514</c>) or a key (<c>23503</c>/<c>23505</c>) and stays the 500 it should be: the reason to
    /// catch this one is that "that money is not free" is a fact about an account, not a bug in a write,
    /// and a customer who is short deserves the sentence rather than a failure.
    /// </remarks>
    private static bool IsDrawRefusedByTheDatabase(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.RaiseException };

    /// <summary>
    /// The core's sentence, which is never null on a refusal it made.
    /// </summary>
    /// <remarks>
    /// <see cref="TrySettlePaidOrderResult"/> keeps <c>Reason</c> nullable because settling is its
    /// normal answer, and this handler is the caller that has to turn the other half into a sentence an
    /// operator reads.
    /// </remarks>
    private static string CoreReason(TrySettlePaidOrderResult settled) =>
        settled.Reason ?? "The order would not settle, and the core did not say why.";
}
