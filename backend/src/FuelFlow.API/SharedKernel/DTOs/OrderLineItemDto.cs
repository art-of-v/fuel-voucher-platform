namespace FuelFlow.SharedKernel.DTOs;

public sealed class OrderLineItemDto
{
    public Guid Id { get; set; }
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int UnitPrice { get; set; }
    public int LineTotal { get; set; }
}
