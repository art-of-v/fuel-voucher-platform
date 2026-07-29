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

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var total = await _context.Set<ProviderEventOutbox>().CountAsync(ct);

        var events = await _context.Set<ProviderEventOutbox>()
            .AsNoTracking()
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
