using System.Security.Claims;
using FuelFlow.Features.ErrorLogs.ClearErrorLogs;
using FuelFlow.Features.ErrorLogs.GetErrorLogFacets;
using FuelFlow.Features.ErrorLogs.GetErrorLogs;
using FuelFlow.Features.ErrorLogs.ResolveErrorLogs;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.ErrorLogs;

/// <summary>Which side of the resolved flag the journal should show. An unrecognised value
/// falls back to <see cref="Unresolved"/> rather than to no filter at all.</summary>
public enum ResolutionFilter
{
    Unresolved,
    Resolved,
    All
}

/// <summary>
/// The admin error journal. Reads default to the outstanding errors only: an acknowledged fault
/// has been dealt with, and leaving eleven copies of a handled incident at the top of the list
/// is what buries the next one.
/// </summary>
[ApiController]
[Route("api/admin/errors")]
public sealed class ErrorLogsController : ControllerBase
{
    private readonly GetErrorLogFacetsQueryHandler _getFacetsHandler;
    private readonly GetErrorLogsQueryHandler _getAllHandler;
    private readonly ResolveErrorLogsCommandHandler _resolveHandler;
    private readonly ClearErrorLogsCommandHandler _clearHandler;

    public ErrorLogsController(
        GetErrorLogFacetsQueryHandler getFacetsHandler,
        GetErrorLogsQueryHandler getAllHandler,
        ResolveErrorLogsCommandHandler resolveHandler,
        ClearErrorLogsCommandHandler clearHandler)
    {
        _getFacetsHandler = getFacetsHandler;
        _getAllHandler = getAllHandler;
        _resolveHandler = resolveHandler;
        _clearHandler = clearHandler;
    }

    [HttpGet("facets")]
    [Authorize(Policy = AuthPolicies.Staff)]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var facets = await _getFacetsHandler.HandleAsync(new GetErrorLogFacetsQuery(), ct);

        return Ok(facets);
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.Staff)]
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
        var page = await _getAllHandler.HandleAsync(
            new GetErrorLogsQuery(
                Offset: offset,
                Limit: limit,
                Level: level,
                Source: source,
                TraceId: traceId,
                Resolved: resolved,
                Search: search,
                From: from,
                To: to),
            ct);

        return Ok(page);
    }

    /// <summary>
    /// Marks errors as handled, or reopens them. One call covers a whole incident: a fault fills
    /// the journal with several records (the global handler, the feature handler, EF Core), and
    /// acknowledging them one click at a time is exactly the chore this removes. Re-posting an
    /// id that is already in the requested state is a no-op rather than an error, so a
    /// double-click cannot rewrite who closed an incident.
    /// </summary>
    [HttpPost("resolve")]
    [Authorize(Policy = AuthPolicies.StaffOperations)]
    public async Task<IActionResult> Resolve([FromBody] ResolveErrorLogsRequest request, CancellationToken ct)
    {
        var result = await _resolveHandler.HandleAsync(
            new ResolveErrorLogsCommand(request.Ids, request.Resolved, GetUserId(), GetUserName()),
            ct);

        return result.Status switch
        {
            "NoIds" => ApiError.BadRequest(result.Error),
            "Unauthenticated" => ApiError.Unauthorized("Unauthorized"),
            _ => Ok(new { success = true, changed = result.Changed })
        };
    }

    [HttpDelete]
    [Authorize(Policy = AuthPolicies.StaffOperations)]
    public async Task<IActionResult> Clear([FromQuery] DateTime? before = null, CancellationToken ct = default)
    {
        var deleted = await _clearHandler.HandleAsync(new ClearErrorLogsCommand(before), ct);

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
