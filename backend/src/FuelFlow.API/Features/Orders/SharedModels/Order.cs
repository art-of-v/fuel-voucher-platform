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

    /// <summary>
    /// This order's own key: unique per order, and the external reference the acquirer is given when
    /// the order is refunded. It is NOT the checkout's identity - see <see cref="CheckoutKey"/> for
    /// that - and it must not become one: a refund is matched back to its order through this
    /// reference, so two orders sharing it would make the acquirer's reversal rows ambiguous.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The basket this order is the live attempt for: the customer, the brand, the fuel, the litres
    /// and the quantity (a digest of the whole cart for a bulk checkout). Two orders may not hold the
    /// same basket while both await payment - the index on this column refuses the second one - so a
    /// double tap and a retried request are one order and one invoice instead of two.
    /// </summary>
    /// <remarks>
    /// NULL once the attempt is over: an order that has been paid keeps its key (the index stops
    /// counting it because the order is no longer awaiting payment), and an order that a newer
    /// attempt took over from gives it up, so that the customer can buy the same basket again.
    /// </remarks>
    public string? CheckoutKey { get; set; }
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
