using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands;

public sealed class AuditEventService
{
    private readonly ApplicationDbContext _context;

    public AuditEventService(ApplicationDbContext context) => _context = context;

    public async Task RecordEventAsync(
        string aggregateType,
        string aggregateId,
        string eventType,
        string? oldValue,
        string newValue,
        Guid changedByUserId,
        string? changedByUserName,
        string summary,
        string providerId,
        CancellationToken ct = default)
    {
        _context.Set<AuditEventOutbox>().Add(new AuditEventOutbox
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedByUserId = changedByUserId == Guid.Empty ? null : changedByUserId,
            ChangedByUserName = changedByUserName,
            Summary = summary,
            ChangedAtUtc = DateTime.UtcNow,
            ScopeId = providerId
        });

        await _context.SaveChangesAsync(ct);
    }
}
