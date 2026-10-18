using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Payments.Admin;

/// <summary>
/// The worklist for money we hold that is not yet against a sale.
/// </summary>
/// <remarks>
/// <para>
/// Unallocated payments are invisible to everything else in the system: no order, so no order report,
/// no reconciliation, no lifecycle job. That is precisely why they need a screen — a table nobody
/// opens is a table nobody notices, and "we did not know whose money this was" is a thing said
/// months later.
/// </para>
/// <para>
/// <b>Both jobs an operator has are here.</b> Money with no customer is somebody's, and the operator
/// says whose. Money with a customer but no order is waiting for an order, and the operator says
/// which. Guessing is not offered anywhere — no "best match", no default — because a wrong answer
/// here moves one real person's money to another.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/payments")]
[Authorize(Policy = "Staff")]
public sealed class AdminPaymentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _events;

    public AdminPaymentsController(ApplicationDbContext context, AuditEventService events)
    {
        _context = context;
        _events = events;
    }

    /// <summary>
    /// Money with no order, newest first. Deliberately not filtered by customer: "money belonging to
    /// nobody" is the hardest case and the one an operator arrives to solve.
    /// </summary>
    [HttpGet("unallocated")]
    public async Task<ActionResult<UnallocatedPaymentsResponse>> Unallocated(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var query = _context.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == null && p.Status == PaymentStatus.Succeeded);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.PaidAtUtc)
            .ThenByDescending(p => p.Id)
            .Skip(Math.Max(0, page - 1) * Math.Clamp(limit, 1, 200))
            .Take(Math.Clamp(limit, 1, 200))
            .Select(p => new UnallocatedPaymentDto(
                p.Id,
                p.AmountUah,
                p.Currency,
                p.PaidAtUtc,
                p.Provider,
                p.UserId,
                p.User == null ? null : p.User.PhoneNumber,
                p.User != null && !string.IsNullOrEmpty(p.User.FirstName)
                    ? (p.User.FirstName + " " + p.User.LastName).Trim()
                    : null,
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.Code).FirstOrDefault(),
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.Text).FirstOrDefault(),
                p.SettlementMessages.OrderBy(m => m.MessageId).Select(m => m.TelegramDisplayName).FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(new UnallocatedPaymentsResponse(items, total));
    }

    /// <summary>
    /// Places money that is already held: names its owner, and/or the order it settles. Both optional,
    /// because the two situations are separate — an unrecognised code needs the first, a prepayment
    /// waiting on an order needs the second, and most prepayments need both eventually.
    /// </summary>
    [HttpPost("{id:guid}/attach")]
    public async Task<ActionResult> Attach(Guid id, AttachPaymentRequest request, CancellationToken ct)
    {
        if (request.OrderId is null && request.UserId is null)
            return BadRequest(new { error = "Name the customer, the order, or both." });

        // AsTracking, stated rather than inherited. This mutates the entity and saves, so under a default of
        // NoTracking it would be a silent no-op that still returned 204 - the caller told the money was
        // placed and nothing changed. The guard that enforces this is NoTrackingMutationGuardTests.
        var payment = await _context.Payments
            .AsTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return NotFound();

        if (payment.Status != PaymentStatus.Succeeded)
        {
            // Only real money is placed. Moving a pending or failed row would attach a promise to an
            // order, which is how an order comes to be marked paid by something never collected.
            return BadRequest(new { error = $"Only a succeeded payment can be placed. This one is {payment.Status}." });
        }

        Guid? customerId = payment.UserId;

        if (request.UserId is { } requestedUserId)
        {
            var exists = await _context.Users.AsNoTracking().AnyAsync(u => u.Id == requestedUserId, ct);
            if (!exists) return BadRequest(new { error = "No such customer." });

            if (customerId is not null && customerId != requestedUserId)
            {
                // Reassigning a payment that already names its owner is a different act from placing
                // an anonymous one, and the kind of correction that should be deliberate.
                return BadRequest(new { error = "This payment already belongs to another customer. Refusing to move it." });
            }

            customerId = requestedUserId;
        }

        Guid? orderId = payment.OrderId;

        if (request.OrderId is { } requestedOrderId)
        {
            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == requestedOrderId, ct);
            if (order is null) return BadRequest(new { error = "No such order." });

            if (customerId is { } owner && order.UserId != owner)
                return BadRequest(new { error = "That order belongs to a different customer." });

            if (order.Status != OrderStatus.PendingPayment)
            {
                // Attaching to a paid, fulfilled or cancelled order would mean this money settled
                // something that is already settled.
                return BadRequest(new { error = $"That order is {order.Status} and is not waiting for payment." });
            }

            orderId = requestedOrderId;
        }

        if (orderId == payment.OrderId && customerId == payment.UserId)
        {
            // Nothing to do. Answered rather than refused, because a double-click should not look
            // like a failure.
            return NoContent();
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
        payment.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        await RecordAsync(payment, before, after, ct);

        return NoContent();
    }

    private Task RecordAsync(Payment payment, string before, string after, CancellationToken ct)
    {
        var actorId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : (Guid?)null;

        // No actor, no record: an audit row that cannot name who placed money names nothing. Same
        // short-circuit SuppliersController uses.
        if (actorId is null) return Task.CompletedTask;

        var actorName = User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;

        return _events.RecordEventAsync(
            "Payment", payment.Id.ToString(),
            "PaymentPlaced", before, after,
            actorId.Value, actorName,
            $"Placed {payment.AmountUah:N2} {payment.Currency} declared on {payment.Provider}",
            "all", ct);
    }
}

public sealed record UnallocatedPaymentsResponse(IReadOnlyList<UnallocatedPaymentDto> Data, int Total);

/// <summary>
/// One payment waiting to be placed. Carries what finance actually typed: the operator is resolving a
/// message, and the message is the evidence.
/// </summary>
public sealed record UnallocatedPaymentDto(
    Guid Id,
    decimal AmountUah,
    string Currency,
    DateTime? PaidAtUtc,
    string Provider,
    Guid? UserId,
    string? UserPhone,
    string? UserName,
    string? DeclaredCode,
    string? DeclaredText,
    string? DeclaredBy);

public sealed class AttachPaymentRequest
{
    /// <summary>The order this money pays for.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>The customer this money belongs to, for money declared with an unrecognised code.</summary>
    public Guid? UserId { get; set; }
}