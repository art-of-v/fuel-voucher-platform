namespace FuelFlow.Features.Orders.SharedModels;

using FuelFlow.SharedKernel.Options;

public class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public decimal Price { get; set; }
    public OrderStatus Status { get; set; }

    /// <summary>
    /// What this order represents. Everything created before this column existed is a
    /// <see cref="OrderKind.Purchase"/>; renewals are backfilled by their renewal items and
    /// issuances start with the first voucher handed to a worker.
    /// </summary>
    public OrderKind Kind { get; set; } = OrderKind.Purchase;

    /// <summary>
    /// For an issuance, the purchase the fuel originally came from. Null on purchases and renewals.
    /// </summary>
    public Guid? SourceOrderId { get; set; }
    public string? MonobankInvoiceId { get; set; }

    /// <summary>
    /// The brand's накладна / invoice for an off-platform renewal — the document the customer paid
    /// against when they renewed with the operator instead of in the app. Null for a self-serve
    /// order, where the acquirer's own invoice is <see cref="MonobankInvoiceId"/>.
    /// </summary>
    /// <remarks>
    /// Planning #319: the operator's renewal wrote this on a table of its own. The renewal is an
    /// order like any other, so its settlement document belongs with the order's other money
    /// columns.
    /// </remarks>
    public string? ExternalInvoiceNumber { get; set; }

    /// <summary>The date on that invoice. Null with it.</summary>
    public DateOnly? ExternalInvoiceDate { get; set; }

    public string? MonobankPaymentUrl { get; set; }
    public MonobankStatus? MonobankStatus { get; set; }
    public MonobankMerchant? MonobankMerchant { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public DateTime? PartiallyFulfilledSinceUtc { get; set; }
    public DateTime? LastWebhookProcessedAtUtc { get; set; }
    public DateTime? LastWebhookModifiedDateUtc { get; set; }

    public bool IsDeleted { get; set; }

    public ICollection<Fulfillment> Fulfillments { get; set; } = new List<Fulfillment>();
    public ICollection<OrderLineItem> LineItems { get; set; } = new List<OrderLineItem>();
}
