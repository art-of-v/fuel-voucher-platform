namespace FuelFlow.Features.Orders.CreateCheckout;

using FuelFlow.Features.Orders.SharedModels;

public sealed class CreateCheckoutCommand
{
    public Guid? UserId { get; set; }
    public Guid? LegalEntityId { get; set; }

    /// <summary>
    /// How the customer means to pay. Absent or <c>"Acquirer"</c> sends them to the bank, which is
    /// every checkout this app has ever made; <c>"Declared"</c> is the second button - the order is
    /// written against money the settlement channel holds or expects, and the acquirer is never asked.
    /// </summary>
    /// <remarks>
    /// Anything else is a 400 from the binder, and an order written here keeps that route for good
    /// (see <c>SettlementRoute</c>). Planning #889.
    /// </remarks>
    public SettlementRoute? SettlementRoute { get; set; }

    public Guid FuelTypeId { get; set; }
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
