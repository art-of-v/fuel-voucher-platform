using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders.UpdateOrderStatus;

/// <summary>
/// Changes an order's status on an operator's instruction.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, and the second is the one that matters.
/// </para>
/// <para>
/// <b>The transition has to be one the operator is allowed to make</b> — the state machine owns
/// that list, and it is deliberately short: abandon an unpaid order, or hand a paid order to
/// fulfilment. Everything else the system does by itself.
/// </para>
/// <para>
/// <b>An order that owes fuel has to have paid for it.</b> The endpoint used to write any status
/// at all, so an operator could move an unpaid order into <c>PendingFulfillment</c> — and the
/// fulfilment job, which selects its work by status alone, would hand out vouchers against stock
/// for money that never arrived. The money check lives here and in the fulfilment job, not in
/// the controller: an order is not paid by being told it is.
/// </para>
/// </remarks>
public sealed class UpdateOrderStatusCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateOrderStatusCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UpdateOrderStatusResult> HandleAsync(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.Orders.FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken);
        if (entity is null)
            return UpdateOrderStatusResult.NotFound();

        if (string.IsNullOrWhiteSpace(command.Status) || !Enum.TryParse<OrderStatus>(command.Status, out var parsedStatus))
            return UpdateOrderStatusResult.Refused($"'{command.Status}' is not an order status.");

        if (!OrderStateMachine.CanOperatorTransition(entity.Status, parsedStatus))
            return UpdateOrderStatusResult.Refused(OrderStateMachine.DescribeRefusal(entity.Status, parsedStatus));

        if (OrderStateMachine.RequiresPayment(parsedStatus))
        {
            // The same rule the settle uses, not a weaker copy of it. Asking only whether a capture
            // exists lets an operator hand 10.00 ₴ of a 1200.00 ₴ order over to fulfilment and book
            // nothing, which is the hole planning #867 was written about — seen from the other side.
            // Production on 2026-11-04 has no delivered order whose captures total below its price, so
            // this refuses nothing that was ever legitimately paid.
            if (!await OrderPayment.MoneyOnItPaysForItAsync(_context, entity.Id, cancellationToken))
            {
                var priceUah = FormattableString.Invariant($"{entity.Price:0.00}");
                return UpdateOrderStatusResult.Refused(
                    $"This order's captured payments do not cover its {priceUah} ₴, so it cannot go to "
                    + "fulfilment: the fuel would be handed over for money that has not all arrived. Place "
                    + "the money on the order first — an order whose money is there settles by itself.");
            }
        }

        entity.Status = parsedStatus;
        entity.UpdatedAtUtc = SystemClock.UtcNow;
        _context.Orders.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return UpdateOrderStatusResult.Done();
    }
}
