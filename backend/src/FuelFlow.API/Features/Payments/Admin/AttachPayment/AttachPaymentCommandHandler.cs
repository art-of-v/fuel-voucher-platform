using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

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
/// </remarks>
public sealed class AttachPaymentCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public AttachPaymentCommandHandler(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
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
            // Nothing to do. Answered rather than refused, because a double-click should not look
            // like a failure.
            return AttachPaymentResult.Done();
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
        payment.OrderId = orderId;
        payment.UserId = customerId;
        payment.UpdatedAtUtc = SystemClock.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        await RecordAsync(payment, before, after, command, cancellationToken);

        return AttachPaymentResult.Done();
    }

    private async Task RecordAsync(
        Payment payment,
        string before,
        string after,
        AttachPaymentCommand command,
        CancellationToken cancellationToken)
    {
        // No actor, no record: an audit row that cannot name who placed money names nothing. Same
        // short-circuit SuppliersController uses.
        if (command.ActorId is null) return;

        await _events.RecordEventAsync(
            "Payment", payment.Id.ToString(),
            "PaymentPlaced", before, after,
            command.ActorId.Value, command.ActorName,
            $"Placed {payment.AmountUah:N2} {payment.Currency} declared on {payment.Provider}",
            "all", cancellationToken);
    }
}
