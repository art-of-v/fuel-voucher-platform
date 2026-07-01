namespace FuelFlow.API.Features.Orders.CreateCheckout.Models;

public sealed class CreateCheckoutResponse
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = null!;
    public string? MonobankInvoiceId { get; set; }
    public string? PaymentUrl { get; set; }
}
