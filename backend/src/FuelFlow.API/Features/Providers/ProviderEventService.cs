using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Providers;

public sealed class ProviderEventService
{
    private readonly ApplicationDbContext _context;

    public ProviderEventService(ApplicationDbContext context) => _context = context;

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
        _context.Set<ProviderEventOutbox>().Add(new ProviderEventOutbox
        {
            Id = Guid.NewGuid(),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedByUserId = changedByUserId,
            ChangedByUserName = changedByUserName,
            Summary = summary,
            ChangedAtUtc = DateTime.UtcNow,
            ProviderId = providerId
        });

        await _context.SaveChangesAsync(ct);
    }
}
