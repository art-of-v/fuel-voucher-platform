using System.Security.Claims;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.Receipt;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Orders;

/// <summary>
/// The PDF per-order receipt (#119): the customer's copy and the operator's copy.
/// </summary>
/// <remarks>
/// One controller for both because the document is identical - an operator handing over a paper
/// receipt and a customer downloading it must show the same numbers. Only the scoping differs, and
/// that difference lives entirely in the query, not in the rendering.
/// </remarks>
[ApiController]
public sealed class OrderReceiptController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public OrderReceiptController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>The caller's own receipt.</summary>
    /// <remarks>
    /// Scoped to the caller inside the query rather than checked after the fetch: another customer's
    /// order has to be indistinguishable from one that does not exist, otherwise this endpoint
    /// enumerates other people's order ids by watching which ids return 404 rather than 403.
    /// </remarks>
    [HttpGet("api/purchases/{id:guid}/receipt")]
    [Authorize]
    // The PDF type is declared on the 200 alone. [Produces("application/pdf")] would instead apply
    // content negotiation to every return, and the empty 404 has no pdf formatter to negotiate with -
    // which turns "order not found" into a 406 and hides the real outcome.
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyReceipt(Guid id, CancellationToken cancellationToken)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (claim is null || !Guid.TryParse(claim, out var userId))
        {
            return Unauthorized();
        }

        var receipt = await LoadAsync(o => o.Id == id && o.UserId == userId, cancellationToken);

        // The same response for "not yours" and "not there", as above.
        return receipt is null ? NotFound("Order not found") : Build(receipt);
    }

    /// <summary>Any order, for an operator.</summary>
    [HttpGet("api/admin/orders/{id:guid}/receipt")]
    [Authorize(Policy = "Staff")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnyReceipt(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await LoadAsync(o => o.Id == id, cancellationToken);

        return receipt is null ? NotFound("Order not found") : Build(receipt);
    }

    private IActionResult Build(ReceiptData receipt)
    {
        // Computed once: the two calls below are the same pure function, and calling it twice to
        // share a condition and a value would let them disagree if it ever stopped being pure.
        var refundable = RefundOrderCommandHandler.ComputeRefundAmountKopecks(receipt.Order);

        var document = new OrderReceiptDocument
        {
            OrderId = receipt.Id,
            CreatedAtUtc = receipt.CreatedAtUtc,
            Status = receipt.Status.ToString(),
            // The order's own Price, not a sum of the lines: a partially refunded order leaves the
            // two disagreeing, and the receipt has to say what was actually charged for the goods.
            Total = receipt.Price,
            AcquirerInvoiceId = receipt.MonobankInvoiceId,
            AcquirerName = receipt.MonobankInvoiceId is null ? null : "Monobank",
            AcquirerStatus = receipt.MonobankStatus?.ToString(),
            CompanyName = receipt.CompanyName,
            Lines = receipt.Lines
                .Select(l => new ReceiptLine(l.FuelTypeId, l.Liters, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList(),
            // Kopecks to hryvnia; the receipt is a human-facing document, not a ledger entry.
            Refund = refundable > 0
                ? new RefundReceiptInfo(receipt.UpdatedAtUtc, refundable / 100m, receipt.Id)
                : null
        };

        var pdf = OrderReceiptDocumentBuilder.Build(document);

        return File(pdf, "application/pdf", $"receipt-{receipt.Id:N}.pdf");
    }

    private sealed record ReceiptLineData(string FuelTypeId, decimal Liters, int Quantity, decimal UnitPrice, decimal LineTotal);

    private sealed class ReceiptData
    {
        public required Order Order { get; init; }
        public required Guid Id { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public required DateTime UpdatedAtUtc { get; init; }
        public required decimal Price { get; init; }
        public required OrderStatus Status { get; init; }
        public string? MonobankInvoiceId { get; init; }
        public MonobankStatus? MonobankStatus { get; init; }
        public string? CompanyName { get; init; }
        public required List<ReceiptLineData> Lines { get; init; }
    }

    private async Task<ReceiptData?> LoadAsync(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken)
    {
        // The whole order is loaded because the refund amount is computed from the line items by
        // RefundOrderCommandHandler, and that handler is the single definition of what is refundable.
        // Recomputing it here would let the two drift - and a receipt that disagrees with the refund
        // button in the admin UI is worse than no receipt.
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (order is null)
        {
            return null;
        }

        // Order carries only LegalEntityId, so the company name needs an explicit lookup.
        var companyName = order.LegalEntityId is null
            ? null
            : await _context.LegalEntities
                .Where(le => le.Id == order.LegalEntityId)
                .Select(le => le.Name)
                .FirstOrDefaultAsync(cancellationToken);

        return new ReceiptData
        {
            Order = order,
            Id = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            Price = order.Price,
            Status = order.Status,
            MonobankInvoiceId = order.MonobankInvoiceId,
            MonobankStatus = order.MonobankStatus,
            CompanyName = companyName,
            Lines = order.LineItems
                .Select(li => new ReceiptLineData(li.FuelTypeId, li.Liters, li.Quantity, li.UnitPrice, li.LineTotal))
                .ToList()
        };
    }
}