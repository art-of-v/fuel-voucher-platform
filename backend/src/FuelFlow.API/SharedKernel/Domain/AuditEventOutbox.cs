namespace FuelFlow.SharedKernel.Domain;

public sealed class AuditEventOutbox : IAppendOnlyEntity
{
    public Guid Id { get; set; }
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;
    public string ScopeId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = null!;
    /// <summary>
    /// Who changed it, when a person did. NULL when a machine did - the price sync writes
    /// <see cref="Guid.Empty"/>, which is not a user and never was.
    /// <para>
    /// NULL rather than the all-zero guid, because that is what makes the foreign key
    /// possible: a column that has to carry "no user" as a value that looks like a user id
    /// cannot be checked against the users table at all.
    /// </para>
    /// </summary>
    public Guid? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public string Summary { get; set; } = null!;
    public DateTime ChangedAtUtc { get; set; }
}
