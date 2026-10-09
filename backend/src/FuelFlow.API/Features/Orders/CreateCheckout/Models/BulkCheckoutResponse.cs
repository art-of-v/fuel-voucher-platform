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
}
