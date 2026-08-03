namespace FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;

public sealed class MonobankInvoiceStatus
{
    public string InvoiceId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int? Amount { get; set; }
    public int? FinalAmount { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? FailureReason { get; set; }
    public List<MonobankCancelListItem>? CancelList { get; set; }
}

public sealed class MonobankCancelListItem
{
    public string Status { get; set; } = null!;
    public int? Amount { get; set; }
    public int? Ccy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public string? ApprovalCode { get; set; }
    public string? Rrn { get; set; }
    public string? ExtRef { get; set; }
}
