namespace FuelFlow.Api.Features.VoucherImport;

public record ImportJob
{
    public Guid Id { get; init; }
    public string? AdminId { get; init; }
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int SuccessfulFiles { get; init; }
    public int FailedFiles { get; init; }
    public int DuplicateVouchers { get; init; }
    public string Status { get; init; } = "processing";
    public string? ModelUsed { get; init; }
    public string? ErrorLog { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
