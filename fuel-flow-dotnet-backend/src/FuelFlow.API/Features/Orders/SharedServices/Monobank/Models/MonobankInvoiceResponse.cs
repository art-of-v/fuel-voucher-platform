namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceResponse
{
    public string InvoiceId { get; set; } = null!;
    public string PageUrl { get; set; } = null!;
}
