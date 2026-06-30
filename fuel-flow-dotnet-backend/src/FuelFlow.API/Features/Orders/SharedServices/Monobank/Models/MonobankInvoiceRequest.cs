namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceRequest
{
    public int Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public string? WebhookUrl { get; set; }
    public string? RedirectUrl { get; set; }
    public string? MerchantPaymentInfo { get; set; }
}
