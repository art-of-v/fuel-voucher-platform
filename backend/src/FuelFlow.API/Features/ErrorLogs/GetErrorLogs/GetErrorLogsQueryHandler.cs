using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs.GetErrorLogs;

/// <summary>
/// Reads the error journal: the rows, filtered and paged.
/// </summary>
/// <remarks>
/// The journal opens on what still needs attention. The free-text search reaches the message,
/// the exception, the source, the request and the trace id, because a fault is looked up by
/// whichever of those the reader happens to have in front of them.
/// </remarks>
public sealed class GetErrorLogsQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetErrorLogsQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<ErrorLogsResponse> HandleAsync(
        GetErrorLogsQuery query,
        CancellationToken ct = default)
    {
        var errors = _context.ErrorLogs.AsNoTracking();

        // The filters are copied into locals first: the compiler keeps a local's "not null" state
        // inside a lambda, and a property's it does not - so the copies are what keeps the
        // Contains(...) calls below free of nullability warnings.
        var level = query.Level;
        var source = query.Source;
        var traceId = query.TraceId;
        var search = query.Search;
        var from = query.From;
        var to = query.To;

        // A numeric value outside the enum binds successfully, so an unrecognised one must not
        // fall through to "no filter" and quietly dump the whole journal at a caller who asked
        // for something narrower. Same default as a caller who passes nothing.
        var resolved = query.Resolved;
        if (resolved is not (ResolutionFilter.Unresolved or ResolutionFilter.Resolved or ResolutionFilter.All))
            resolved = ResolutionFilter.Unresolved;

        if (!string.IsNullOrWhiteSpace(level))
            errors = errors.Where(e => e.Level == level);
        if (!string.IsNullOrWhiteSpace(source))
            errors = errors.Where(e => e.Source != null && e.Source.Contains(source));
        if (!string.IsNullOrWhiteSpace(traceId))
            errors = errors.Where(e => e.TraceId == traceId);
        if (resolved == ResolutionFilter.Unresolved)
            errors = errors.Where(e => e.ResolvedAtUtc == null);
        else if (resolved == ResolutionFilter.Resolved)
            errors = errors.Where(e => e.ResolvedAtUtc != null);
        if (!string.IsNullOrWhiteSpace(search))
            errors = errors.Where(e =>
                e.Message.Contains(search)
                || (e.ExceptionType != null && e.ExceptionType.Contains(search))
                || (e.ExceptionMessage != null && e.ExceptionMessage.Contains(search))
                || (e.Source != null && e.Source.Contains(search))
                || (e.RequestPath != null && e.RequestPath.Contains(search))
                || (e.TraceId != null && e.TraceId.Contains(search))
                || (e.UserName != null && e.UserName.Contains(search)));
        if (from.HasValue)
            errors = errors.Where(e => e.LoggedAtUtc >= from.Value);
        if (to.HasValue)
            errors = errors.Where(e => e.LoggedAtUtc <= to.Value);

        var total = await errors.CountAsync(ct);

        var items = await errors
            .OrderByDescending(e => e.LoggedAtUtc)
            // The tie-break: many log rows share one timestamp, and without a unique second key
            // the sort is unstable - a row can land on two pages or on none.
            .ThenBy(e => e.Id)
            .Skip(PageLimits.ClampOffset(query.Offset))
            .Take(PageLimits.ClampPageSize(query.Limit))
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

        return new ErrorLogsResponse(total, items);
    }
}
