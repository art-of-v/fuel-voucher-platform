using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;

namespace FuelFlow.Features.Orders.Receipt.GetOrderReceipt;

/// <summary>
/// Everything the receipt document is built from, read out of the database in one go.
/// </summary>
public sealed class OrderReceiptData
{
    public required Guid Id { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
    public required DateTime RefundedAtUtc { get; init; }
    public required decimal Price { get; init; }
    public required OrderStatus Status { get; init; }
    public string? MonobankInvoiceId { get; init; }
    public MonobankStatus? MonobankStatus { get; init; }
    public string? CompanyName { get; init; }
    public required List<OrderReceiptLineData> Lines { get; init; }

    /// <summary>
    /// How much of the order can still be given back, in kopecks.
    /// </summary>
    /// <remarks>
    /// Computed by <see cref="RefundOrderCommandHandler.ComputeRefundAmountKopecks"/> - the single
    /// definition of what is refundable. The receipt has to agree with the refund button in the
    /// admin UI, so the two must not be able to drift apart.
    /// </remarks>
    public required decimal RefundableKopecks { get; init; }
}

/// <summary>
/// One paid line of the order, reduced to what the payment document prints: how many codes, at what
/// price, for what sum. The fuel and the volume stay out - they describe the code, not the sale.
/// </summary>
public sealed record OrderReceiptLineData(
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
