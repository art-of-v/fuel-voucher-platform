namespace FuelFlow.Features.Orders.CreateCheckout;

public sealed class BulkCheckoutCommand
{
    public Guid? UserId { get; set; }
    public List<CheckoutItem> Items { get; set; } = new();
}

public sealed class CheckoutItem
{
    public string Provider { get; set; } = null!;
    public string FuelTypeId { get; set; } = null!;
    public decimal Liters { get; set; }
    public int Quantity { get; set; }
    public int Price { get; set; }
    public string? StationId { get; set; }
    public string? StationName { get; set; }
}
