using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SettlePaidOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FuelFlow.Features.Payments.Admin.AttachPayment;

/// <summary>
/// Places money the platform already holds: names its owner, and/or the order it settles.
/// </summary>
/// <remarks>
/// <para>
/// This is the money path, and the refusals are the feature. An unallocated payment is money that
/// belongs to somebody, and every way this can be wrong — wrong customer, wrong order, an order
/// that is already settled — is a way to move a real person's money to the wrong real person.
/// </para>
/// <para>
/// The checks are in this order, and the order is the point: a request that names nothing is
/// refused before the database is asked, a payment that is not real money is refused before its
/// owner is looked at, and the order's customer is checked against the payment's owner before the
/// order's own state — because "that order belongs to somebody else" is the mistake this screen
/// invites, and it is the one an operator has to be told about first.
/// </para>
/// <para>
/// Naming an order is not only a filing act: when the money on the order covers its price, this places
/// it AND settles the order, in the same transaction as the audit row. Before that, the screen that
/// looked like "the money is on the order" left the order waiting for a payment an invoice then expired
/// on — the sequence production read on 2026-11-04 (planning #867).
/// </para>
/// <para>
/// Only money nobody has drawn on can be named to an order. A declared transfer is a balance, and a
/// balance is spent by settling orders out of it; a transfer that has already paid for two orders has
/// what is left of it and no order it belongs to. Attaching it whole would tell the money gate to count
/// the same banknote a second time, so the write is refused by the database and said here in the figures
/// an operator needs — drawn, free, and where the difference went.
/// </para>
/// </remarks>
public sealed class AttachPaymentCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;
    private readonly TrySettlePaidOrderCommandHandler _settle;
    private readonly ILogger<AttachPaymentCommandHandler> _logger;

    public AttachPaymentCommandHandler(
        ApplicationDbContext context,
        AuditEventService events,
        TrySettlePaidOrderCommandHandler settle,
        ILogger<AttachPaymentCommandHandler> logger)
    {
        _context = context;
        _events = events;
        _settle = settle;
        _logger = logger;
    }

    public async Task<AttachPaymentResult> HandleAsync(
        AttachPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OrderId is null && command.UserId is null)
            return AttachPaymentResult.Refused("Name the customer, the order, or both.");

        // AsTracking, stated rather than inherited. This mutates the entity and saves, so under a default of
        // NoTracking it would be a silent no-op that still returned 204 - the caller told the money was
        // placed and nothing changed. The guard that enforces this is NoTrackingMutationGuardTests.
        var payment = await _context.Payments
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, cancellationToken);
        if (payment is null) return AttachPaymentResult.NotFound();

        if (payment.Status != PaymentStatus.Succeeded)
        {
            // Only real money is placed. Moving a pending or failed row would attach a promise to an
            // order, which is how an order comes to be marked paid by something never collected.
            return AttachPaymentResult.Refused($"Only a succeeded payment can be placed. This one is {payment.Status}.");
        }

        Guid? customerId = payment.UserId;

        if (command.UserId is { } requestedUserId)
        {
            var exists = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == requestedUserId, cancellationToken);
            if (!exists) return AttachPaymentResult.Refused("No such customer.");

            if (customerId is not null && customerId != requestedUserId)
            {
                // Reassigning a payment that already names its owner is a different act from placing
                // an anonymous one, and the kind of correction that should be deliberate.
                return AttachPaymentResult.Refused("This payment already belongs to another customer. Refusing to move it.");
            }

            customerId = requestedUserId;
        }

        Guid? orderId = payment.OrderId;

        if (command.OrderId is { } requestedOrderId)
        {
            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == requestedOrderId, cancellationToken);
            if (order is null) return AttachPaymentResult.Refused("No such order.");

            if (customerId is { } owner && order.UserId != owner)
                return AttachPaymentResult.Refused("That order belongs to a different customer.");

            if (payment.OrderId == requestedOrderId && customerId == payment.UserId)
            {
                // The same press again, and it has to be answered before the order's own state is
                // checked, because that state is what a successful press moved: refusing a double-click
                // with "That order is PendingFulfillment and is not waiting for payment" would report
                // the work this screen just did as a failure.
                //
                // The order's state is still heard, but from the settle's answer rather than from a copy
                // of the rule written here. Money on a terminal order is the stranded shape production
                // showed on 2026-11-04, and that is a refusal the operator has to see; an order already
                // handed to fulfilment is the press having finished its job. (planning #867)
                return await _context.ExecuteReplaySafeAsync(
                    token => TrySettleAsync(requestedOrderId, payment, "an operator pressed place again", token),
                    cancellationToken);
            }

            if (order.Status != OrderStatus.PendingPayment)
            {
                // Attaching to a paid, fulfilled or cancelled order would mean this money settled
                // something that is already settled.
                return AttachPaymentResult.Refused($"That order is {order.Status} and is not waiting for payment.");
            }

            orderId = requestedOrderId;
        }

        if (orderId == payment.OrderId && customerId == payment.UserId)
        {
            // Nothing to place, and no order named to settle. Answered rather than refused, because a
            // double-click should not look like a failure.
            return AttachPaymentResult.Done();
        }

        if (orderId is not null)
        {
            // A transfer that has already paid for orders is a balance with part of it committed, and
            // attaching it would tell the money gate to count the WHOLE amount on this one order. The
            // database refuses that write — assert_spent_money_is_not_attached — and the refusal is
            // said here so an operator's press ends in a sentence about their customer's money instead
            // of a 500 about a rule nobody ever showed them.
            var spent = await _context.PaymentAllocations
                .AsNoTracking()
                .Where(a => a.PaymentId == payment.Id)
                .Select(a => a.AmountUah)
                .ToListAsync(cancellationToken);

            if (spent.Count > 0)
            {
                var drawn = spent.Sum();
                var free = payment.AmountUah - drawn;

                _logger.LogInformation(
                    "Payment {PaymentId} cannot be placed on order {OrderId}: {Drawn} ₴ of it is "
                     + "already drawn on {Orders} order(s)",
                    payment.Id, orderId, Uah(drawn), spent.Count);

                return AttachPaymentResult.Refused(
                    $"This transfer has already paid for {spent.Count} order(s): {Uah(drawn)} ₴ drawn and "
                    + $"{Uah(free)} ₴ free. It cannot be attached to a further order — that counts the same "
                    + "money twice. Pay the order out of the balance instead.");
            }
        }

        var before = JsonSerializer.Serialize(new
        {
            order_id = payment.OrderId?.ToString(),
            user_id = payment.UserId?.ToString(),
        });
        var after = JsonSerializer.Serialize(new
        {
            order_id = orderId?.ToString(),
            user_id = customerId?.ToString(),
        });

        // The money write and the settle are one unit. Placed-and-unsettled is not a halfway state an
        // operator can be left in: the order still reads as awaiting its payment, which keeps its
        // acquirer invoice live, and the invoice expiring is what cancelled a paid order in production
        // on 2026-11-04. (planning #867)
        //
        // No verifySucceeded: this body is idempotent by its own keys. A commit that landed while the
        // answer was lost is replayed, the payment write sets the same values, the outbox probe finds
        // the event the first attempt wrote, and the journal entry is unique on
        // (source_event, source_type, source_id) - so the replay converges instead of doubling.
        var outcome = await _context.ExecuteReplaySafeAsync(
            async token =>
            {
                payment.OrderId = orderId;
                payment.UserId = customerId;
                payment.UpdatedAtUtc = SystemClock.UtcNow;

                // The audit row is tracked before the save, so it commits in the same transaction as
                // the money it describes (planning #533).
                Record(payment, before, after, command);

                await _context.SaveChangesAsync(token);

                return await TrySettleAsync(orderId, payment, "an operator placed it on the order", token);
            },
            cancellationToken);

        return outcome;
    }

    /// <summary>
    /// Settles the order the money was just placed on, if the money covers it.
    /// </summary>
    /// <remarks>
    /// Placing money is the act that means "this is paid" - so it has to do the four things that makes
    /// true, or the panel is a second, quieter way of leaving an order unpaid. Most declines are not
    /// failures of this press and the money stays placed: half the price on an order is real money that
    /// belongs there, and the order still owes for what is missing; an order already handed to
    /// fulfilment was settled by whoever got here first. What is refused is an order that cannot be
    /// settled at all — money on a cancelled order is money the operator has to be told about, not a
    /// 204.
    /// </remarks>
    private async Task<AttachPaymentResult> TrySettleAsync(
        Guid? orderId, Payment payment, string because, CancellationToken token)
    {
        if (orderId is null)
        {
            // Money with an owner and no order is the held state, and the held state is correct: there
            // is nothing here to settle, and guessing which of the customer's orders this pays for is
            // the SettlementUpdateHandler's refusal, not one this screen gets to make.
            return AttachPaymentResult.Done();
        }

        var answer = await _settle.HandleAsync(
            new TrySettlePaidOrderCommand
            {
                OrderId = orderId.Value,
                Because = FormattableString.Invariant($"{because} ({payment.AmountUah:0.00} ₴)"),
            },
            token);

        if (answer.Refusal is SettleRefusal.OrderRefusesSettlement or SettleRefusal.NoSuchOrder)
        {
            _logger.LogWarning(
                "Payment {PaymentId} is on order {OrderId} which will not settle: {Reason}",
                payment.Id, orderId, answer.Reason);

            return AttachPaymentResult.Refused(
                $"The money is placed, but the order will not settle: {answer.Reason}");
        }

        if (!answer.Settled)
        {
            _logger.LogInformation(
                "Payment {PaymentId} placed on order {OrderId} without settling it: {Reason}",
                payment.Id, orderId, answer.Reason);
        }

        return AttachPaymentResult.Done();
    }

    /// <summary>A money figure in a sentence an operator reads, in the currency's format and not the server's culture.</summary>
    private static string Uah(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private void Record(
        Payment payment,
        string before,
        string after,
        AttachPaymentCommand command)
    {
        // No actor, no record: an audit row that cannot name who placed money names nothing. Same
        // short-circuit SuppliersController uses.
        if (command.ActorId is null) return;

        _events.RecordEvent(
            "Payment", payment.Id.ToString(),
            "PaymentPlaced", before, after,
            command.ActorId.Value, command.ActorName,
            $"Placed {payment.AmountUah:N2} {payment.Currency} declared on {payment.Provider}",
            "all");
    }
}
