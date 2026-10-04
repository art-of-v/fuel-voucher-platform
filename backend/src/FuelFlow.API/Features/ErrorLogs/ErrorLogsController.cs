using System.Security.Claims;
using System.Text.Json;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs;

/// <summary>
/// The admin error journal. Reads default to the outstanding errors only: an acknowledged fault
/// has been dealt with, and leaving eleven copies of a handled incident at the top of the list
/// is what buries the next one.
/// </summary>
[ApiController]
[Route("api/admin/errors")]
[Authorize(Policy = "Staff")]
public sealed class ErrorLogsController : ControllerBase
{
    /// <summary>Which side of the resolved flag the journal should show. An unrecognised value
    /// falls back to <see cref="Unresolved"/> rather than to no filter at all.</summary>
    public enum ResolutionFilter
    {
        Unresolved,
        Resolved,
        All
    }

    /// <summary>Upper bound on ids in one resolve call, so a crafted request cannot turn a single
    /// POST into an unbounded UPDATE ... WHERE id = ANY(...).</summary>
    private const int MaxIdsPerResolveCall = 500;

    private readonly ApplicationDbContext _context;
    private readonly ProviderEventService _eventService;

    public ErrorLogsController(ApplicationDbContext context, ProviderEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var levels = await _context.ErrorLogs
            .AsNoTracking()
            .Select(e => e.Level)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var sources = await _context.ErrorLogs
            .AsNoTracking()
            .Where(e => e.Source != null)
            .Select(e => e.Source!)
            .Distinct()
            .OrderBy(x => x)
            .Take(200)
            .ToListAsync(ct);

        return Ok(new { levels, sources });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        [FromQuery] string? level = null,
        [FromQuery] string? source = null,
        [FromQuery] string? traceId = null,
        [FromQuery] ResolutionFilter resolved = ResolutionFilter.Unresolved,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = _context.ErrorLogs.AsNoTracking();

        // A numeric value outside the enum binds successfully, so an unrecognised one must not
        // fall through to "no filter" and quietly dump the whole journal at a caller who asked
        // for something narrower. Same default as a caller who passes nothing.
        if (resolved is not (ResolutionFilter.Unresolved or ResolutionFilter.Resolved or ResolutionFilter.All))
            resolved = ResolutionFilter.Unresolved;

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(e => e.Source != null && e.Source.Contains(source));
        if (!string.IsNullOrWhiteSpace(traceId))
            query = query.Where(e => e.TraceId == traceId);
        if (resolved == ResolutionFilter.Unresolved)
            query = query.Where(e => e.ResolvedAtUtc == null);
        else if (resolved == ResolutionFilter.Resolved)
            query = query.Where(e => e.ResolvedAtUtc != null);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Message.Contains(search)
                || (e.ExceptionType != null && e.ExceptionType.Contains(search))
                || (e.ExceptionMessage != null && e.ExceptionMessage.Contains(search))
                || (e.Source != null && e.Source.Contains(search))
                || (e.RequestPath != null && e.RequestPath.Contains(search))
                || (e.TraceId != null && e.TraceId.Contains(search))
                || (e.UserName != null && e.UserName.Contains(search)));
        if (from.HasValue)
            query = query.Where(e => e.LoggedAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.LoggedAtUtc <= to.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(e => e.LoggedAtUtc)
            .Skip(PageLimits.ClampOffset(offset))
            .Take(PageLimits.ClampPageSize(limit))
            .Select(e => new ErrorLogDto
            {
                Id = e.Id,
                LoggedAtUtc = e.LoggedAtUtc,
                Level = e.Level,
                Message = e.Message,
                ExceptionType = e.ExceptionType,
                ExceptionMessage = e.ExceptionMessage,
                StackTrace = e.StackTrace,
                Source = e.Source,
                RequestPath = e.RequestPath,
                RequestMethod = e.RequestMethod,
                UserName = e.UserName,
                TraceId = e.TraceId,
                ResolvedAtUtc = e.ResolvedAtUtc,
                ResolvedByUserId = e.ResolvedByUserId,
                ResolvedByUserName = e.ResolvedByUserName
            })
            .ToListAsync(ct);

        return Ok(new { total, items });
    }

    /// <summary>
    /// Marks errors as handled, or reopens them. One call covers a whole incident: a fault fills
    /// the journal with several records (the global handler, the feature handler, EF Core), and
    /// acknowledging them one click at a time is exactly the chore this removes. Re-posting an
    /// id that is already in the requested state is a no-op rather than an error, so a
    /// double-click cannot rewrite who closed an incident.
    /// </summary>
    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve([FromBody] ResolveErrorLogsRequest request, CancellationToken ct)
    {
        if (request.Ids is not { Count: > 0 })
            return BadRequest(new { error = "No error ids supplied" });

        var actingId = GetUserId();
        if (actingId is null)
            return Unauthorized();

        var ids = request.Ids.Distinct().Take(MaxIdsPerResolveCall).ToList();

        // ExecuteUpdateAsync bypasses change tracking, which matters here because the global
        // query default is NoTracking (DatabaseSetup) - a loaded-then-mutated row would save
        // nothing at all and the journal would look like it had swallowed the request.
        var query = _context.ErrorLogs.Where(e => ids.Contains(e.Id));
        query = request.Resolved
            ? query.Where(e => e.ResolvedAtUtc == null)
            : query.Where(e => e.ResolvedAtUtc != null);

        var actingName = GetUserName();
        var changed = await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.ResolvedAtUtc,
                    request.Resolved ? DateTime.UtcNow : (DateTime?)null)
                .SetProperty(e => e.ResolvedByUserId,
                    request.Resolved ? actingId : (Guid?)null)
                .SetProperty(e => e.ResolvedByUserName,
                    request.Resolved ? actingName : null),
            ct);

        if (changed > 0)
        {
            // ProviderId is required and an error record has none, so the acting user stands in -
            // the same workaround the user and QA-access aggregates already use.
            await _eventService.RecordEventAsync(
                "ErrorLog",
                ids.Count == 1 ? ids[0].ToString() : $"bulk:{changed}",
                request.Resolved ? "ErrorLogsResolved" : "ErrorLogsReopened",
                null,
                JsonSerializer.Serialize(new { count = changed, resolved = request.Resolved }),
                actingId.Value,
                actingName,
                $"{(request.Resolved ? "Resolved" : "Reopened")} {changed} error log record(s)",
                actingId.Value.ToString(),
                ct);
        }

        return Ok(new { success = true, changed });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear([FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        IQueryable<ErrorLog> query = _context.ErrorLogs;
        if (before.HasValue)
            query = query.Where(e => e.LoggedAtUtc <= before.Value);

        var deleted = await query.ExecuteDeleteAsync(ct);
        return Ok(new { deleted });
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? GetUserName()
    {
        var first = User.FindFirst("first_name")?.Value;
        var last = User.FindFirst("last_name")?.Value;
        if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            return $"{first} {last}".Trim();

        return User.FindFirst(ClaimTypes.Name)?.Value;
    }
}

public sealed record ErrorLogDto
{
    public Guid Id { get; init; }
    public DateTime LoggedAtUtc { get; init; }
    public string Level { get; init; } = null!;
    public string Message { get; init; } = null!;
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? StackTrace { get; init; }
    public string? Source { get; init; }
    public string? RequestPath { get; init; }
    public string? RequestMethod { get; init; }
    public string? UserName { get; init; }
    public string? TraceId { get; init; }
    public DateTime? ResolvedAtUtc { get; init; }
    public Guid? ResolvedByUserId { get; init; }
    public string? ResolvedByUserName { get; init; }
}

public sealed record ResolveErrorLogsRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public bool Resolved { get; init; } = true;
}
