using System.Security.Claims;
using FuelFlow.Features.Payments.Admin.AttachPayment;
using FuelFlow.Features.Payments.Admin.GetCustomerByCode;
using FuelFlow.Features.Payments.Admin.GetUnallocatedPayments;
using FuelFlow.Features.Payments.Admin.SearchCustomers;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly GetUnallocatedPaymentsQueryHandler _unallocatedHandler;
    private readonly SearchCustomersQueryHandler _customersHandler;
    private readonly GetCustomerByCodeQueryHandler _customerByCodeHandler;
    private readonly AttachPaymentCommandHandler _attachHandler;

    public AdminPaymentsController(
        GetUnallocatedPaymentsQueryHandler unallocatedHandler,
        SearchCustomersQueryHandler customersHandler,
        GetCustomerByCodeQueryHandler customerByCodeHandler,
        AttachPaymentCommandHandler attachHandler)
    {
        _unallocatedHandler = unallocatedHandler;
        _customersHandler = customersHandler;
        _customerByCodeHandler = customerByCodeHandler;
        _attachHandler = attachHandler;
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
        var response = await _unallocatedHandler.HandleAsync(
            new GetUnallocatedPaymentsQuery(page, limit), ct);

        return Ok(response);
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
        var hits = await _customersHandler.HandleAsync(new SearchCustomersQuery(search, limit), ct);

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
        var result = await _customerByCodeHandler.HandleAsync(new GetCustomerByCodeQuery(code), ct);

        if (!result.CodeIsValid)
            return BadRequest(new { error = result.Error });

        if (result.Customer is null)
        {
            return NotFound(new
            {
                error = result.Error,
            });
        }

        var customer = result.Customer;

        return Ok(new
        {
            customer.Id,
            customer.PhoneNumber,
            customer.SettlementCode,
            Name = customer.Name,
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
        var result = await _attachHandler.HandleAsync(
            new AttachPaymentCommand(
                id,
                request.OrderId,
                request.UserId,
                ActorId(),
                ActorName()),
            ct);

        if (!result.Found) return NotFound();
        if (!result.Success) return BadRequest(new { error = result.Error });

        return NoContent();
    }

    /// <summary>
    /// Who is placing the money. The claims are the controller's to read — a handler is told who,
    /// not how to find out.
    /// </summary>
    private Guid? ActorId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : (Guid?)null;

    /// <summary>
    /// The name the audit row carries: the operator's first name, or their login.
    /// </summary>
    private string? ActorName() =>
        User.FindFirst("first_name")?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;
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
