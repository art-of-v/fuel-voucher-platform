namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class CreateCheckoutCommand
{
    public Guid? UserId { get; set; }
    public Guid? LegalEntityId { get; set; }

    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }

    /// <summary>
    /// The fuel brand, as a Brand id. This is what the line's brand is written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brand used to be a second field named <c>Provider</c>, required, and then ignored:
    /// <c>CreateCheckoutCommandHandler</c> wrote <c>Brand = command.BrandId</c>. Two clients
    /// disagreed with each other and with the server - the app sent <c>provider: 'MONOBANK'</c>, a
    /// payment provider, to satisfy a 400 whose reason it could not see, while Postman sent
    /// <c>provider: "okko"</c>, a brand. The validation rejected requests the handler would have
    /// accepted anyway.
    /// </para>
    /// <para>
    /// Nothing read the duplicate, so it is gone. The published app still sends it and the field is
    /// ignored on deserialisation, which keeps 1.0.1 and the 1.1.0 build working against this
    /// contract.
    /// </para>
    /// <para>
    /// A <see cref="Guid"/>, not the network's code: brands are keyed by uuid (planning D-21), and the
    /// order line's <c>Brand</c> column is still written as the code for the reason the remarks below
    /// describe - a voucher, a paper and an order line all carry a readable brand, and only the join
    /// needs the id.
    /// </para>
    /// </remarks>
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
}
