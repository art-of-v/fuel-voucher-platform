namespace FuelFlow.API.Features.Orders.CreateCheckout.Models;

public sealed class BulkCheckoutResponse
{
    public List<Guid> OrderIds { get; set; } = new();
    public string? MonobankInvoiceId { get; set; }

    /// <summary>Embeddable payment page (see <c>MonobankInvoiceResponse.PageUrl</c>).</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>
    /// Universal link that opens the Monobank app on the confirmation screen, for the
    /// "Pay via mono" button. Null unless the Monobank API returned one.
    /// </summary>
    public string? AppUrl { get; set; }

    /// <summary>
    /// The order is paid and the customer has nothing left to pay, because the acquirer was never
    /// asked for an invoice: money the finance channel had already declared for this customer, at
    /// exactly this price, was sitting unattached when this order was written, and it moved onto it
    /// (planning #867, trigger 2).
    /// </summary>
    /// <remarks>
    /// See the same field on <c>CreateCheckoutResponse</c> — carrying it here is what lets the cart
    /// path, which is the one the app actually uses, tell the customer the truth instead of leaving
    /// them with a missing payment link.
    /// </remarks>
    public bool AlreadyPaid { get; set; }
}
