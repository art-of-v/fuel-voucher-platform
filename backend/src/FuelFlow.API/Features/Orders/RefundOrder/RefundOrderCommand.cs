namespace FuelFlow.API.Features.Orders.RefundOrder;

public sealed class RefundOrderCommand
{
    public Guid OrderId { get; set; }
    public int? AmountKopecks { get; set; }
    public string? Reason { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public bool IsAutomatic { get; set; }
}

public sealed class RefundOrderResult
{
    public Guid RefundId { get; set; }
    public Guid OrderId { get; set; }
    public int AmountKopecks { get; set; }
    public string Status { get; set; } = null!;
    public string? ErrorMessage { get; set; }
}
