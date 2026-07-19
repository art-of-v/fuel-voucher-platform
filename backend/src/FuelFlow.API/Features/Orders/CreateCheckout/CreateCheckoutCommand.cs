namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class CreateCheckoutCommand
{
    public Guid? UserId { get; set; }
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string? StationId { get; set; }
    public string? StationName { get; set; }
}
