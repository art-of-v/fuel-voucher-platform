namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommand
{
    public Guid? UserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public List<CheckoutItem> Items { get; set; } = new();
}

public sealed class CheckoutItem
{
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? StationId { get; set; }
    public string? StationName { get; set; }

    /// <summary>
    /// The term of validity the customer is buying this fuel for (<c>1w</c>…<c>6m</c>), which earns a
    /// bigger discount the shorter it is. Null or absent means the voucher's full remaining supplier
    /// term at the undiscounted price — the behaviour when the term-sale feature is off.
    /// </summary>
    public string? TermCode { get; set; }
}
