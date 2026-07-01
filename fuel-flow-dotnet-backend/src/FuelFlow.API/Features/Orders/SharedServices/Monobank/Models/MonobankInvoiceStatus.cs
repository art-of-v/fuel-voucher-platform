namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceStatus
{
    public string InvoiceId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int? Amount { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
