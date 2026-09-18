using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs;

[ApiController]
[Route("api/admin/errors")]
[Authorize(Policy = "Staff")]
public sealed class ErrorLogsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ErrorLogsController(ApplicationDbContext context) => _context = context;

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
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = _context.ErrorLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(e => e.Source != null && e.Source.Contains(source));
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e =>
                e.Message.Contains(search)
                || (e.ExceptionType != null && e.ExceptionType.Contains(search))
                || (e.ExceptionMessage != null && e.ExceptionMessage.Contains(search))
                || (e.Source != null && e.Source.Contains(search))
                || (e.RequestPath != null && e.RequestPath.Contains(search))
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
                UserName = e.UserName
            })
            .ToListAsync(ct);

        return Ok(new { total, items });
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
}
