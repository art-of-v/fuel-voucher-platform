namespace FuelFlow.Features.Orders.SharedModels;

public class OrderLineItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int LineTotal { get; set; }

    public Order Order { get; set; } = null!;
}
