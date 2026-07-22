namespace FuelFlow.API.Features.Orders.CreateCheckout.Models;

public sealed class BulkCheckoutResponse
{
    public List<Guid> OrderIds { get; set; } = new();
    public string? MonobankInvoiceId { get; set; }
    public string? PaymentUrl { get; set; }
}
