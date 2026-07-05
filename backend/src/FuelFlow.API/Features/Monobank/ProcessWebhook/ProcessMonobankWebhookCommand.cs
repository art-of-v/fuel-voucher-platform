namespace FuelFlow.Features.Monobank.ProcessWebhook;

public sealed class ProcessMonobankWebhookCommand
{
    public string InvoiceId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public long Amount { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string? Signature { get; set; }
    public string? RawBody { get; set; }
}
