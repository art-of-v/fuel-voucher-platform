using System.Security.Claims;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.Receipt;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Payments.SharedModels;
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
                .Select(l => new ReceiptLine(
                    // The line stores a fuel type ID, which for real fuels is a GUID. The receipt
                    // prints what the customer bought, so the name comes from the catalog.
                    receipt.FuelNames.TryGetValue(l.FuelTypeId, out var name) ? name : l.FuelTypeId.ToString(),
                    l.Liters, l.Quantity, l.UnitPrice, l.LineTotal))
                .ToList(),
            Refund = BuildRefundSection(receipt.Status, refundable, receipt.RefundedAtUtc, receipt.Id)
        };

        var pdf = OrderReceiptDocumentBuilder.Build(document);

        return File(pdf, "application/pdf", $"receipt-{receipt.Id:N}.pdf");
    }

    /// <summary>
    /// The refund section of the document, or null when this document is not a refund.
    /// </summary>
    /// <remarks>
    /// Two conditions, and both are load-bearing. The order has to be in a refunded state, because
    /// a merely refundable order is an ordinary payment whose refund button is still enabled in the
    /// admin UI - printing the section there told a customer their own receipt had been returned.
    /// And the amount has to be positive, so an order that is marked refunded but has nothing left
    /// to give back prints no made-up figure.
    /// </remarks>
    internal static RefundReceiptInfo? BuildRefundSection(
        OrderStatus status,
        decimal refundableKopecks,
        DateTime refundedAtUtc,
        Guid orderId)
    {
        if (status is not (OrderStatus.Refunded or OrderStatus.PartiallyRefunded))
        {
            return null;
        }

        if (refundableKopecks <= 0)
        {
            return null;
        }

        // Kopecks to hryvnia; the receipt is a human-facing document, not a ledger entry.
        return new RefundReceiptInfo(refundedAtUtc, refundableKopecks / 100m, orderId);
    }

    private sealed record ReceiptLineData(Guid FuelTypeId, decimal Liters, int Quantity, decimal UnitPrice, decimal LineTotal);

    private sealed class ReceiptData
    {
        public required Order Order { get; init; }
        public required Guid Id { get; init; }
        public required DateTime CreatedAtUtc { get; init; }
        public required DateTime UpdatedAtUtc { get; init; }
        public required DateTime RefundedAtUtc { get; init; }
        public required decimal Price { get; init; }
        public required OrderStatus Status { get; init; }
        public string? MonobankInvoiceId { get; init; }
        public MonobankStatus? MonobankStatus { get; init; }
        public string? CompanyName { get; init; }
        public required List<ReceiptLineData> Lines { get; init; }

        /// <summary>
        /// Fuel type id to the name a customer should read, e.g. "3808d410-..." to "А 95+".
        /// </summary>
        public required IReadOnlyDictionary<Guid, string> FuelNames { get; init; }
    }

    private async Task<ReceiptData?> LoadAsync(
        System.Linq.Expressions.Expression<Func<Order, bool>> predicate,
        CancellationToken cancellationToken)
    {
        // The whole order is loaded because the refund amount is computed from the line items by
        // RefundOrderCommandHandler, and that handler is the single definition of what is refundable.
        // Recomputing it here would let the two drift - and a receipt that disagrees with the refund
        // button in the admin UI is worse than no receipt.
        //
        // Fulfillments + Voucher are part of that graph, not an extra. ComputeRefundAmountKopecks
        // subtracts what has been delivered from what was paid; without them EF hands back an EMPTY
        // collection, every unit reads as undelivered, and the function reports the entire order
        // value as still refundable.
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.LineItems)
            .Include(o => o.Fulfillments)
                .ThenInclude(f => f.Voucher)
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

        // The line stores the fuel type ID, which for real fuels is a GUID. Printing it makes the
        // receipt's fuel column unreadable, so the names come from the catalog the customer saw.
        var fuelIds = order.LineItems.Select(li => li.FuelTypeId).Distinct().ToList();
        var fuelNames = await _context.FuelTypes
            .Where(ft => fuelIds.Contains(ft.Id))
            .ToDictionaryAsync(ft => ft.Id, ft => ft.Name, cancellationToken);

        // The refund row knows when the refund was actually recorded; UpdatedAtUtc moves on any
        // later edit to the order, which is not when the money went back.
var refundedAtUtc = await _context.Payments
    .Where(p => p.OrderId == order.Id && p.Kind == PaymentKind.Refund)
    .OrderByDescending(p => p.CreatedAtUtc)
    .Select(p => (DateTime?)p.CreatedAtUtc)
    .FirstOrDefaultAsync(cancellationToken) ?? order.UpdatedAtUtc;

        return new ReceiptData
        {
            Order = order,
            Id = order.Id,
            CreatedAtUtc = order.CreatedAtUtc,
            UpdatedAtUtc = order.UpdatedAtUtc,
            RefundedAtUtc = refundedAtUtc,
            Price = order.Price,
            Status = order.Status,
            MonobankInvoiceId = order.MonobankInvoiceId,
            MonobankStatus = order.MonobankStatus,
            CompanyName = companyName,
            FuelNames = fuelNames,
            Lines = order.LineItems
                .Select(li => new ReceiptLineData(li.FuelTypeId, li.Liters, li.Quantity, li.UnitPrice, li.LineTotal))
                .ToList()
        };
    }
}