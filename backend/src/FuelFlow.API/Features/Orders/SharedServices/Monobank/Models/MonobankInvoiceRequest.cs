namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceRequest
{
    // Kopecks. long so bulk invoices cannot overflow int (~21.5M UAH).
    public long Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public string? WebhookUrl { get; set; }
    public string? RedirectUrl { get; set; }
    public string? MerchantPaymentInfo { get; set; }

    /// <summary>
    /// The purchase description printed on the receipt. Defaults to a fuel purchase when unset -
    /// never to the internal order reference, which is what `reference` is for.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>
    /// The receipt's goods lines, as <c>merchantPaymInfo.basketOrder</c>. See
    /// <see cref="MonobankBasketItem"/> for why these are always sent.
    /// </summary>
    public List<MonobankBasketItem>? Items { get; set; }
}
