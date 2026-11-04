using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.SettlePaidOrder;

/// <summary>
/// Settles an order on the strength of the money attached to it, and says so when it will not.
/// </summary>
/// <remarks>
/// <para>
/// This is the door that planning #867 was opened for. Until it existed, only an acquirer could
/// settle: <c>SettlePaidOrderCommand</c> was called from the webhook and from the reconciliation poll
/// that feeds it, and nothing else — so money that arrived by another channel could be attached to an
/// order and sit there unpaid forever, and an operator who then moved the order by hand got fuel
/// issued with no <c>ORDER_CREATED</c> event and no receipt in the books.
/// </para>
/// <para>
/// <b>It answers one question and refuses everything else.</b> The order must be in a state the settle
/// is allowed to leave (the state machine's own rule, so the acquirer's path and this one cannot drift
/// apart), and the captures attached to it must total at least its price. Two orders whose money
/// arrived in halves therefore settle on the second half, not the first; one order paid in full by a
/// transfer that also covered a second order settles neither.
/// </para>
/// <para>
/// <b>It takes an id and loads the order itself.</b> Its callers each hold the money from a different
/// direction — the settlement channel has a payment row and a resolved customer, the panel has an
/// operator's request — and neither should have to know that the outbox payload needs the line items, or
/// that a hidden order that got paid has to come back into view. Those are the settle's business, so the
/// load that makes them possible is here.
/// </para>
/// <para>
/// The transaction is the caller's, as it is for the core: a caller that wraps this in
/// <c>ExecuteReplaySafeAsync</c> gets money, order and books in one unit. A caller that does not is
/// committing the order and its receipt separately, which is exactly the hole #867 describes, so both
/// callers it has today wrap it.
/// </para>
/// </remarks>
public sealed class TrySettlePaidOrderCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly SettlePaidOrderCommandHandler _settle;
    private readonly ILogger<TrySettlePaidOrderCommandHandler> _logger;

    public TrySettlePaidOrderCommandHandler(
        ApplicationDbContext context,
        SettlePaidOrderCommandHandler settle,
        ILogger<TrySettlePaidOrderCommandHandler> logger)
    {
        _context = context;
        _settle = settle;
        _logger = logger;
    }

    public async Task<TrySettlePaidOrderResult> HandleAsync(
        TrySettlePaidOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters because a paid order the customer cannot see is still a paid order: the
        // core restores it, and a caller that could not load it would silently drop the money.
        var order = await _context.Orders
            .IgnoreQueryFilters()
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken);

        if (order is null)
        {
            return TrySettlePaidOrderResult.Declined(SettleRefusal.NoSuchOrder, "No such order.");
        }

        if (order.Status == OrderStatus.PendingFulfillment)
        {
            // Not a failure: the order is already where a settle would put it, and the money it was
            // settled on is the acquirer's business or an earlier call's. Answering rather than
            // re-running keeps a redelivered message from restamping the order's UpdatedAtUtc.
            return TrySettlePaidOrderResult.Declined(
                SettleRefusal.AlreadyHandedOver, "The order is already handed to fulfilment.");
        }

        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.PendingFulfillment))
        {
            return TrySettlePaidOrderResult.Declined(
                SettleRefusal.OrderRefusesSettlement,
                $"The order is {order.Status}, which is not a state money can settle.");
        }

        if (!await OrderPayment.MoneyOnItPaysForItAsync(_context, order.Id, cancellationToken))
        {
            // The figure is what the caller has to chase, so it prints with a dot in every locale.
            return TrySettlePaidOrderResult.Declined(
                SettleRefusal.MoneyIsShort,
                FormattableString.Invariant($"The money on it does not yet cover its {order.Price:0.00} ₴."));
        }

        await _settle.HandleAsync(new SettlePaidOrderCommand { Order = order }, cancellationToken);

        _logger.LogInformation(
            "SETTLEMENTS: order {OrderId} settled — {Because}. No acquirer said anything about it.",
            order.Id, command.Because);

        return TrySettlePaidOrderResult.Done();
    }
}
