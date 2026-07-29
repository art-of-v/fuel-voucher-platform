namespace FuelFlow.SharedKernel.Domain;

public sealed class ProviderEventOutbox
{
    public Guid Id { get; set; }
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = null!;
    public Guid ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public string Summary { get; set; } = null!;
    public DateTime ChangedAtUtc { get; set; }
}
