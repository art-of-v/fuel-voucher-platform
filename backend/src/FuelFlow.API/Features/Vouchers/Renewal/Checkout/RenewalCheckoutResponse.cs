namespace FuelFlow.Features.Vouchers.Renewal.Checkout;

/// <summary>Result of creating a renewal checkout: the pending order plus its Monobank invoice.</summary>
public sealed class RenewalCheckoutResponse
{
    public Guid OrderId { get; set; }
    public string? MonobankInvoiceId { get; set; }
    public string? PaymentUrl { get; set; }

    /// <summary>Whole-UAH batch total the customer will be charged.</summary>
    public int TotalUah { get; set; }
}
