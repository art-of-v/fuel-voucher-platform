using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Audit.GetAuditFacets;

/// <summary>
/// The distinct values of the three filterable columns of the audit journal.
/// </summary>
/// <remarks>
/// The authors are the ones who have a name: a row written by the system itself has none, and an
/// empty entry in the "who changed this" dropdown is not a person.
/// </remarks>
public sealed class GetAuditFacetsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAuditFacetsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<AuditFacetsResponse> HandleAsync(
        GetAuditFacetsQuery query,
        CancellationToken ct = default)
    {
        var eventTypes = await _context.Set<AuditEventOutbox>()
            .AsNoTracking()
            .Select(e => e.EventType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var aggregateTypes = await _context.Set<AuditEventOutbox>()
            .AsNoTracking()
            .Select(e => e.AggregateType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var users = await _context.Set<AuditEventOutbox>()
            .AsNoTracking()
            .Where(e => e.ChangedByUserName != null)
            .Select(e => e.ChangedByUserName!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        return new AuditFacetsResponse(eventTypes, aggregateTypes, users);
    }
}
