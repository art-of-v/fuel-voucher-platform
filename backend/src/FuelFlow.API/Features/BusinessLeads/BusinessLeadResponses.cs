namespace FuelFlow.Features.BusinessLeads;

public sealed record AdminBusinessLeadDto
{
    public Guid Id { get; init; }
    public string CompanyName { get; init; } = null!;
    public string ContactName { get; init; } = null!;
    public string Phone { get; init; } = null!;
    public string? Email { get; init; }
    public int? FleetSize { get; init; }
    public string? Message { get; init; }
    public string Status { get; init; } = null!;
    public DateTime? EmailSentAtUtc { get; init; }
    public string? EmailSendError { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed record BusinessLeadListResponse(int Total, IReadOnlyList<AdminBusinessLeadDto> Items);
public sealed record BusinessLeadDetailResponse(AdminBusinessLeadDto Item);
public sealed record BusinessLeadFacetsResponse(
    IReadOnlyList<string> Statuses,
    int Total,
    int Active,
    int New);
public sealed record BusinessLeadAttentionResponse(int Count);
public sealed record BusinessLeadMutationResponse(bool Success, int Affected);
public sealed record UpdateBusinessLeadStatusRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public string Status { get; init; } = "Contacted";
}
