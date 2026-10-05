namespace FuelFlow.SharedKernel.DTOs;

public sealed class PurchaseDto
{
    public Guid Id { get; set; }
    public string ProductType { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string FuelType { get; set; } = null!;
    public string FuelName { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankPaymentUrl { get; set; }
    public string? MonobankStatus { get; set; }
    public Guid? LegalEntityId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public List<VoucherDto>? Vouchers { get; set; }
    public List<OrderLineItemDto> LineItems { get; set; } = new();

    /// <summary>
    /// True when this order is a voucher renewal/replacement (it has
    /// voucher_renewal_items rows) rather than a fuel purchase. Lets the mobile
    /// app label it distinctly and keep the renewed voucher in the primary list.
    /// </summary>
    public bool IsRenewal { get; set; }

    /// <summary>
    /// What the order represents: <c>Purchase</c>, <c>Renewal</c>, or
    /// <c>ReceivedFromCompany</c> — a handover of already-bought fuel to a worker. The mobile
    /// app needs it to tell a worker's handover receipt apart from a purchase they made.
    /// </summary>
    public string Kind { get; set; } = "Purchase";
}
