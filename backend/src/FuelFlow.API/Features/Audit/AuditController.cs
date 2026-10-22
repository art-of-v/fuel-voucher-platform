using FuelFlow.Features.Audit.GetAuditEvents;
using FuelFlow.Features.Audit.GetAuditFacets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Audit;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Policy = "Staff")]
public sealed class AuditController : ControllerBase
{
    private readonly GetAuditEventsQueryHandler _getAllHandler;
    private readonly GetAuditFacetsQueryHandler _getFacetsHandler;

    public AuditController(
        GetAuditEventsQueryHandler getAllHandler,
        GetAuditFacetsQueryHandler getFacetsHandler)
    {
        _getAllHandler = getAllHandler;
        _getFacetsHandler = getFacetsHandler;
    }

    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var facets = await _getFacetsHandler.HandleAsync(new GetAuditFacetsQuery(), ct);

        return Ok(facets);
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
        var page = await _getAllHandler.HandleAsync(
            new GetAuditEventsQuery(offset, limit, eventType, aggregateType, changedBy, search, from, to),
            ct);

        return Ok(page);
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
