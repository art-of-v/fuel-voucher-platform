using FuelFlow.Features.Orders.SharedModels;

namespace FuelFlow.Features.Orders.UpdateMonobankInfo;

public sealed class UpdateMonobankInfoCommand
{
    public Guid OrderId { get; set; }
    public string InvoiceId { get; set; } = null!;
    public MonobankStatus Status { get; set; }
}
