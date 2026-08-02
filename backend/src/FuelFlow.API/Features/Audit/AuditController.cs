using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Audit;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = "Admin")]
public sealed class AuditController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AuditController(ApplicationDbContext context) => _context = context;

    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var eventTypes = await _context.Set<ProviderEventOutbox>()
            .AsNoTracking()
            .Select(e => e.EventType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var aggregateTypes = await _context.Set<ProviderEventOutbox>()
            .AsNoTracking()
            .Select(e => e.AggregateType)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var users = await _context.Set<ProviderEventOutbox>()
            .AsNoTracking()
            .Where(e => e.ChangedByUserName != null)
            .Select(e => e.ChangedByUserName!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        return Ok(new { eventTypes, aggregateTypes, users });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        [FromQuery] string? eventType = null,
        [FromQuery] string? aggregateType = null,
        [FromQuery] string? changedBy = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = _context.Set<ProviderEventOutbox>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);
        if (!string.IsNullOrWhiteSpace(aggregateType))
            query = query.Where(e => e.AggregateType == aggregateType);
        if (!string.IsNullOrWhiteSpace(changedBy))
            query = query.Where(e => e.ChangedByUserName != null && e.ChangedByUserName.Contains(changedBy));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Summary.Contains(search)
                || e.AggregateId.Contains(search)
                || e.EventType.Contains(search)
                || (e.ChangedByUserName != null && e.ChangedByUserName.Contains(search)));
        if (from.HasValue)
            query = query.Where(e => e.ChangedAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.ChangedAtUtc <= to.Value);

        var total = await query.CountAsync(ct);

        var events = await query
            .OrderByDescending(e => e.ChangedAtUtc)
            .Skip(offset)
            .Take(limit)
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

        return Ok(new { total, events });
    }
}

public sealed record AuditEventDto
{
    public Guid Id { get; init; }
    public string AggregateType { get; init; } = null!;
    public string AggregateId { get; init; } = null!;
    public string EventType { get; init; } = null!;
    public string? ChangedByUserName { get; init; }
    public string Summary { get; init; } = null!;
    public DateTime ChangedAtUtc { get; init; }
}
