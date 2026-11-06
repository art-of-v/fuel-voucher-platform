namespace FuelFlow.API.Features.Orders.CreateCheckout.Models;

public sealed class CreateCheckoutResponse
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = null!;
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
    /// With it the client is told the fact rather than made to infer it from a missing
    /// <see cref="PaymentUrl"/>, which is not an answer — an older bundle reads that absence as a
    /// payment failure. Nothing here is charged twice either way; what the flag decides is whether the
    /// customer is sent to a payment sheet for money they have already paid.
    /// </remarks>
    public bool AlreadyPaid { get; set; }
}
