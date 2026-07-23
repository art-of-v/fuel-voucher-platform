namespace FuelFlow.Features.Orders.SharedModels;

public class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Price { get; set; }
    public OrderStatus Status { get; set; }
    public string? MonobankInvoiceId { get; set; }
    public string? MonobankPaymentUrl { get; set; }
    public MonobankStatus? MonobankStatus { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }

    public ICollection<Fulfillment> Fulfillments { get; set; } = new List<Fulfillment>();
    public ICollection<OrderLineItem> LineItems { get; set; } = new List<OrderLineItem>();
}
