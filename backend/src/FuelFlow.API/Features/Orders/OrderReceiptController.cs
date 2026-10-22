using System.Security.Claims;
using FuelFlow.Features.Orders.Receipt;
using FuelFlow.Features.Orders.Receipt.GetOrderReceipt;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly GetOrderReceiptQueryHandler _handler;

    public OrderReceiptController(GetOrderReceiptQueryHandler handler)
    {
        _handler = handler;
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
            return ApiError.Unauthorized("Unauthorized");
        }

        var receipt = await _handler.HandleAsync(
            GetOrderReceiptQuery.ForCaller(id, userId), cancellationToken);

        // The same response for "not yours" and "not there", as above.
        return receipt is null ? ApiError.NotFound("Order not found") : Build(receipt);
    }

    /// <summary>Any order, for an operator.</summary>
    [HttpGet("api/admin/orders/{id:guid}/receipt")]
    [Authorize(Policy = "Staff")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnyReceipt(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await _handler.HandleAsync(
            GetOrderReceiptQuery.ForAnyOrder(id), cancellationToken);

        return receipt is null ? ApiError.NotFound("Order not found") : Build(receipt);
    }

    private IActionResult Build(OrderReceiptData receipt)
    {
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
            Refund = BuildRefundSection(receipt.Status, receipt.RefundableKopecks, receipt.RefundedAtUtc, receipt.Id)
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
}
