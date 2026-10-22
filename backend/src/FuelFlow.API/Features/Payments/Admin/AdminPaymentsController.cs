using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Time;
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
/// <para>
/// <b>These endpoints carry the settlement capability, not the read surface.</b> Their rows carry a
/// customer's settlement code — the code a message declared, the code of a customer offered to the
/// picker — and that code is what the settlement channel resolves money by. It is money data, so the
/// roles that may see it are the money tier (<c>AuthPolicies.SettlementCode</c>), and the endpoints
/// that serve it say so rather than borrowing the blanket read policy.
/// </para>
/// </remarks>
[ApiController]
[Route("api/admin/payments")]
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
    [Authorize(Policy = AuthPolicies.SettlementCode)]
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

        // One extra query for the whole page, not one per row. The alternative is an operator pasting
        // an order id, which is not placing money — it is asking them to trust a UUID.
        var customerIds = items.Select(i => i.UserId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        var openOrders = customerIds.Count == 0
            ? []
            : await _context.Orders
                .AsNoTracking()
                .Where(o => customerIds.Contains(o.UserId) && o.Status == OrderStatus.PendingPayment)
                .OrderByDescending(o => o.CreatedAtUtc)
                .Select(o => new OpenOrderRefDto(o.Id, o.UserId, o.Price, o.CreatedAtUtc))
                .ToListAsync(ct);

        return Ok(new UnallocatedPaymentsResponse(items, total, openOrders));
    }

    /// <summary>
    /// Searches customers by name, phone or settlement code, bounded.
    /// </summary>
    /// <remarks>
    /// Deliberately not the existing <c>GET /api/admin/users</c>. That one materialises the whole
    /// table — its handler ends in <c>ToList()</c> with no limit — which is fine for a screen that
    /// shows everyone once and unusable as a search-as-you-type backing query. Fixing that endpoint
    /// properly is a separate change with its own callers; this is a bounded query for one picker.
    /// <para>
    /// The same fields are searched as the users screen, plus the settlement code, because the
    /// operator arriving at this screen has one of the two in front of them.
    /// </para>
    /// </remarks>
    [HttpGet("customers")]
    [Authorize(Policy = AuthPolicies.SettlementCode)]
    public async Task<ActionResult<IReadOnlyList<CustomerSearchHit>>> Customers(
        [FromQuery] string search,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var term = (search ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length < 2)
        {
            // Two characters minimum. A one-character search matches most of the table, and the
            // point of a bounded query is defeated the moment the bound is the result set.
            return Ok(Array.Empty<CustomerSearchHit>());
        }

        var capped = Math.Clamp(limit, 1, 50);

        var hits = await _context.Users
            .AsNoTracking()
            .Where(u =>
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(term)) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)) ||
                (u.SettlementCode != null && u.SettlementCode.ToLower().Contains(term)))
            // Settlement code first, then a name, then the phone: an exact code is the most certain
            // match a person can type, and an exact match should not sit below a lucky substring.
            .OrderByDescending(u => u.SettlementCode == term.ToUpperInvariant())
            .ThenBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Take(capped)
            .Select(u => new CustomerSearchHit(
                u.Id,
                u.PhoneNumber,
                u.SettlementCode,
                string.IsNullOrEmpty(u.FirstName) && string.IsNullOrEmpty(u.LastName)
                    ? null
                    : (u.FirstName + " " + u.LastName).Trim()))
            .ToListAsync(ct);

        return Ok(hits);
    }

    /// <summary>
    /// Finds the customer a settlement code names, or says plainly that it names nobody.
    /// </summary>
    /// <remarks>
    /// The workflow this exists for: finance typed a code that did not match, and the operator needs
    /// to know whether that is a typo in a code that does exist, or a code for a customer who does not.
    /// Those are different problems with different fixes, and "not found" alone does not tell them
    /// which. Deliberately no name or phone search — that is a customer list with its own pagination,
    /// and a partial version of it here would be a worse version of one that already exists.
    /// </remarks>
    [HttpGet("customers/by-code")]
    [Authorize(Policy = AuthPolicies.SettlementCode)]
    public async Task<ActionResult> CustomerByCode([FromQuery] string code, CancellationToken ct)
    {
        var normalised = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalised.Length != 8)
            return BadRequest(new { error = "A settlement code is eight characters." });

        var customer = await _context.Users
            .AsNoTracking()
            .Where(u => u.SettlementCode == normalised)
            .Select(u => new { u.Id, u.PhoneNumber, u.FirstName, u.LastName, u.SettlementCode })
            .FirstOrDefaultAsync(ct);

        if (customer is null)
        {
            return NotFound(new
            {
                error = $"No customer has the code {normalised}. It was either mistyped, or it belongs to a customer who has not registered yet.",
            });
        }

        return Ok(new
        {
            customer.Id,
            customer.PhoneNumber,
            customer.SettlementCode,
            Name = string.IsNullOrEmpty(customer.FirstName) && string.IsNullOrEmpty(customer.LastName)
                ? null
                : (customer.FirstName + " " + customer.LastName).Trim(),
        });
    }

    /// <summary>
    /// Places money that is already held: names its owner, and/or the order it settles. Both optional,
    /// because the two situations are separate — an unrecognised code needs the first, a prepayment
    /// waiting on an order needs the second, and most prepayments need both eventually.
    /// </summary>
    [HttpPost("{id:guid}/attach")]
    [Authorize(Policy = AuthPolicies.MoneyMovement)]
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
        payment.UpdatedAtUtc = SystemClock.UtcNow;

        // Tracked before the save: the row and the payment it describes commit together, or neither
        // does (planning #533).
        Record(payment, before, after);

        await _context.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Tracks the audit row for a payment placed by hand. Committed by the caller's own save, so the
    /// row and the change it describes land together (planning #533).
    /// </summary>
    private void Record(Payment payment, string before, string after)
    {
        var actorId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : (Guid?)null;

        // No actor, no record: an audit row that cannot name who placed money names nothing. Same
        // short-circuit SuppliersController uses.
        if (actorId is null) return;

        var actorName = User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;

        _events.RecordEvent(
            "Payment", payment.Id.ToString(),
            "PaymentPlaced", before, after,
            actorId.Value, actorName,
            $"Placed {payment.AmountUah:N2} {payment.Currency} declared on {payment.Provider}",
            "all");
    }
}

/// <summary>
/// The worklist plus every open order belonging to a customer on it, so the operator picks an order
/// from a list rather than assembling a UUID.
/// </summary>
public sealed record UnallocatedPaymentsResponse(
    IReadOnlyList<UnallocatedPaymentDto> Data,
    int Total,
    IReadOnlyList<OpenOrderRefDto> OpenOrders);

/// <summary>One order still waiting for money. The price is here so the operator can match it to the amount.</summary>
public sealed record OpenOrderRefDto(Guid Id, Guid UserId, decimal Price, DateTime CreatedAtUtc);

/// <summary>A customer offered by the picker. The settlement code is shown because it is what identifies them on the channel.</summary>
public sealed record CustomerSearchHit(Guid Id, string? PhoneNumber, string? SettlementCode, string? Name);

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