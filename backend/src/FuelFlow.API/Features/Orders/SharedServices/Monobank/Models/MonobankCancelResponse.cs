namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankCancelResponse
{
    public string? Status { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
