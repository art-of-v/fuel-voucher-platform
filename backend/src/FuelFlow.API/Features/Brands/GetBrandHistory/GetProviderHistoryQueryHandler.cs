using FuelFlow.Features.Brands.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetProviderHistory;

public sealed class GetBrandHistoryQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetBrandHistoryQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<AuditEventDto>> HandleAsync(GetBrandHistoryQuery query, CancellationToken ct = default)
    {
        var events = await _context.Set<AuditEventOutbox>()
            .AsNoTracking()
            .Where(e => e.ScopeId == query.ScopeId)
            .OrderByDescending(e => e.ChangedAtUtc)
            .Take(PageLimits.ClampPageSize(query.Limit))
            .Select(e => new AuditEventDto
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
