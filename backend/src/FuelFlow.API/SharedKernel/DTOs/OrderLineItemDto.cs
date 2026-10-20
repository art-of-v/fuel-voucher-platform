namespace FuelFlow.SharedKernel.DTOs;

public sealed class OrderLineItemDto
{
    public Guid Id { get; set; }
    public string Brand { get; set; } = null!;
    public Guid FuelTypeId { get; set; }
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
