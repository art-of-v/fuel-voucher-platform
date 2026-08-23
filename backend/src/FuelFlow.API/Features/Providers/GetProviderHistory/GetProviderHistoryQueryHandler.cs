using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Providers.GetProviderHistory;

public sealed class GetProviderHistoryQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetProviderHistoryQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<ProviderEventDto>> HandleAsync(GetProviderHistoryQuery query, CancellationToken ct = default)
    {
        var events = await _context.Set<ProviderEventOutbox>()
            .AsNoTracking()
            .Where(e => e.ProviderId == query.ProviderId)
            .OrderByDescending(e => e.ChangedAtUtc)
            .Take(PageLimits.ClampPageSize(query.Limit))
            .Select(e => new ProviderEventDto
            {
                Id = e.Id,
                AggregateType = e.AggregateType,
                AggregateId = e.AggregateId,
                EventType = e.EventType,
                OldValue = e.OldValue,
                NewValue = e.NewValue,
                ChangedByUserName = e.ChangedByUserName,
                Summary = e.Summary,
                ChangedAtUtc = e.ChangedAtUtc
            })
            .ToListAsync(ct);

        return events;
    }
}
