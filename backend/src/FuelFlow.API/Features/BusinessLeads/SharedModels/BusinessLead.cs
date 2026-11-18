namespace FuelFlow.Features.BusinessLeads.SharedModels;

public enum BusinessLeadStatus
{
    New,
    Contacted,
    Qualified,
    Converted,
    Rejected
}

/// <summary>A company or fleet asking Fuel Flow to contact them.</summary>
public sealed class BusinessLead
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = null!;
    public string ContactName { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public int? FleetSize { get; set; }
    public string? Message { get; set; }
    public BusinessLeadStatus Status { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? EmailSentAtUtc { get; set; }
    public string? EmailSendError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
