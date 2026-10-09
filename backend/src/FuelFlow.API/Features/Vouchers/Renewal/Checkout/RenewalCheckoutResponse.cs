namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>Result of creating a renewal checkout: the pending order plus its Monobank invoice.</summary>
public sealed class RenewalCheckoutResponse
{
    public Guid OrderId { get; set; }
    public string? MonobankInvoiceId { get; set; }

    /// <summary>Embeddable payment page (see <c>MonobankInvoiceResponse.PageUrl</c>).</summary>
    public string? PaymentUrl { get; set; }

    /// <summary>
    /// Universal link that opens the Monobank app on the confirmation screen, for the
    /// "Pay via mono" button. Null unless the Monobank API returned one.
    /// </summary>
    public string? AppUrl { get; set; }

    /// <summary>Batch total (decimal UAH to the kopeck) the customer will be charged.</summary>
    public decimal TotalUah { get; set; }
}
