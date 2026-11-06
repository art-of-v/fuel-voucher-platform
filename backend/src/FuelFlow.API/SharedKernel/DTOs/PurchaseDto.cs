namespace FuelFlow.SharedKernel.DTOs;

public sealed class PurchaseDto
{
    public Guid Id { get; set; }
    public string ProductType { get; set; } = null!;
    public string Brand { get; set; } = null!;
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
    /// True when this order is a voucher renewal/replacement (one of its lines names
    /// the voucher it renews) rather than a fuel purchase. Lets the mobile
    /// app label it distinctly and keep the renewed voucher in the primary list.
    /// </summary>
    public bool IsRenewal { get; set; }

    /// <summary>
    /// What the order represents: <c>Purchase</c>, <c>Renewal</c>, or
    /// <c>ReceivedFromCompany</c> — a handover of already-bought fuel to a worker. The mobile
    /// app needs it to tell a worker's handover receipt apart from a purchase they made.
    /// </summary>
    public string Kind { get; set; } = "Purchase";

    /// <summary>
    /// Which of the two buttons wrote this order: <c>Acquirer</c> or <c>Declared</c>. The wallet needs it
    /// because an order waiting on the settlement channel and a payment the bank never opened have the
    /// same body — no invoice, no payment URL — and without this field the first one gets the second
    /// one's sentence, which ends in "buy again" (planning #889, item 6).
    /// </summary>
    /// <remarks>
    /// It is the route, not the money: the settlement code that identifies an account on the finance
    /// channel is staff-only and stays out of every customer read, which
    /// <c>SettlementCodeTests</c> checks by type. An order from before the column existed is reported as
    /// <c>Acquirer</c>, which is what it was.
    /// </remarks>
    public string SettlementRoute { get; set; } = null!;
}
