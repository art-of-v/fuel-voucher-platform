namespace FuelFlow.Features.Orders.SharedModels;

public sealed class Refund
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid UserId { get; set; }
    public int Amount { get; set; }
    public string InvoiceId { get; set; } = null!;
    public string ExtRef { get; set; } = null!;
    public RefundStatus Status { get; set; }
    public string? MonobankStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByUserName { get; set; }
    public bool IsAutomatic { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Order Order { get; set; } = null!;
}

public enum RefundStatus
{
    Processing,
    Completed,
    Failed
}
