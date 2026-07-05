namespace FuelFlow.Features.Orders.SharedModels;

public class Fulfillment
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid VoucherId { get; set; }
    public DateTime FulfilledAtUtc { get; set; }
}
