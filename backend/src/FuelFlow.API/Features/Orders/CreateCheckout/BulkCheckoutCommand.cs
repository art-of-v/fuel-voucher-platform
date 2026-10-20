namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommand
{
    public Guid? UserId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public List<CheckoutItem> Items { get; set; } = new();
}

public sealed class CheckoutItem
{
    public Guid FuelTypeId { get; set; }
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }

    /// <summary>
    /// The fuel brand, as a Brand id. The line's brand is written from this, so it is the only
    /// place the brand travels.
    /// </summary>
    /// <remarks>
    /// The item also used to carry a required <c>Provider</c> that the handler overwrote with this
    /// value - see <see cref="CreateCheckoutCommand.BrandId"/> for why the duplicate went away.
    /// </remarks>
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }

    /// <summary>
    /// The term of validity the customer is buying this fuel for (<c>1w</c>…<c>6m</c>), which earns a
    /// bigger discount the shorter it is. Null or absent means the voucher's full remaining supplier
    /// term at the undiscounted price — the behaviour when the term-sale feature is off.
    /// </summary>
    public string? TermCode { get; set; }
}
