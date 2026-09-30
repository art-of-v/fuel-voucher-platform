namespace FuelFlow.SharedKernel.DTOs;

public sealed class OrderLineItemDto
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
