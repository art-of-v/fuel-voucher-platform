using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Audit.GetAuditEvents;

/// <summary>
/// Reads the audit journal: the rows, filtered and paged.
/// </summary>
/// <remarks>
/// The filters are the same shape as the ones the admin's dropdowns offer, and every one of them
/// narrows: a filter that matches nothing shows nothing. The free-text one reaches the summary,
/// the aggregate id, the event type and the author, because an operator remembers a row by any
/// one of those and typing the wrong field should not hide the row they are looking for.
/// </remarks>
public sealed class GetAuditEventsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAuditEventsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<AuditEventsResponse> HandleAsync(
        GetAuditEventsQuery query,
        CancellationToken ct = default)
    {
        var events = _context.Set<AuditEventOutbox>().AsNoTracking();

        // The filters are copied into locals first: the compiler keeps a local's "not null" state
        // inside a lambda, and a property's it does not - so the copies are what keeps the
        // Contains(...) calls below free of nullability warnings.
        var eventType = query.EventType;
        var aggregateType = query.AggregateType;
        var changedBy = query.ChangedBy;
        var search = query.Search;
        var from = query.From;
        var to = query.To;

        if (!string.IsNullOrWhiteSpace(eventType))
            events = events.Where(e => e.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(aggregateType))
            events = events.Where(e => e.AggregateType == aggregateType);
        if (!string.IsNullOrWhiteSpace(changedBy))
            events = events.Where(e => e.ChangedByUserName != null && e.ChangedByUserName.Contains(changedBy));
        if (!string.IsNullOrWhiteSpace(search))
            events = events.Where(e =>
                e.Summary.Contains(search)
                || e.AggregateId.Contains(search)
                || e.EventType.Contains(search)
                || (e.ChangedByUserName != null && e.ChangedByUserName.Contains(search)));
        if (from.HasValue)
            events = events.Where(e => e.ChangedAtUtc >= from.Value);
        if (to.HasValue)
            events = events.Where(e => e.ChangedAtUtc <= to.Value);

        var total = await events.CountAsync(ct);

        var page = await events
               .OrderByDescending(e => e.ChangedAtUtc)
               // The tie-break: many audit rows share one timestamp, and without a unique second
               // key the sort is unstable - a row can land on two pages or on none.
               .ThenBy(e => e.Id)
            .Skip(PageLimits.ClampOffset(query.Offset))
            .Take(PageLimits.ClampPageSize(query.Limit))
            .Select(e => new AuditEventDto
            {
                Id = e.Id,
                AggregateType = e.AggregateType,
                AggregateId = e.AggregateId,
                EventType = e.EventType,
                ChangedByUserName = e.ChangedByUserName,
                Summary = e.Summary,
                ChangedAtUtc = e.ChangedAtUtc
            })
            .ToListAsync(ct);

        return new AuditEventsResponse(total, page);
    }
}
