using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.SharedKernel.Database;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.ErrorLogs.ResolveErrorLogs;

/// <summary>
/// Closes the error records of one incident, or reopens the ones that are closed.
/// </summary>
/// <remarks>
/// <para>
/// The write is a bulk update, and it stamps the decision with who made it. Re-posting an id
/// that is already in the requested state is a no-op rather than an error, so a double-click
/// cannot rewrite who closed an incident - and the audit log records the decision, not the
/// click, so a second click adds no second audit row.
/// </para>
/// <para>
/// The flip and the audit row are ONE transaction. The bulk update commits on its own, so
/// without the transaction a resolved error log could exist with no row naming who resolved
/// it - and the row would be a separate write that can fail on its own (planning #533).
/// </para>
/// </remarks>
public sealed class ResolveErrorLogsCommandHandler
{
    /// <summary>Upper bound on ids in one resolve call, so a crafted request cannot turn a single
    /// POST into an unbounded UPDATE ... WHERE id = ANY(...).</summary>
    private const int MaxIdsPerResolveCall = 500;

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public ResolveErrorLogsCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<ResolveErrorLogsResult> HandleAsync(
        ResolveErrorLogsCommand command,
        CancellationToken ct = default)
    {
        if (command.Ids is not { Count: > 0 })
            return ResolveErrorLogsResult.NoIds();

        if (command.ActingUserId is null)
            return ResolveErrorLogsResult.Unauthenticated();

        var actingId = command.ActingUserId.Value;
        var actingName = command.ActingUserName;
        var ids = command.Ids.Distinct().Take(MaxIdsPerResolveCall).ToList();

        // ExecuteUpdateAsync bypasses change tracking by design, so it lands regardless of the query
        // default (TrackAll, DatabaseSetup). That is the point of using it here: the alternative
        // would load rows, mutate them in memory and call SaveChanges, which is a second round-trip
        // for an operation that does not need the entities at all.
        var query = _context.ErrorLogs.Where(e => ids.Contains(e.Id));
        query = command.Resolved
            ? query.Where(e => e.ResolvedAtUtc == null)
            : query.Where(e => e.ResolvedAtUtc != null);

        // One transaction for the flip and the audit row. ExecuteUpdateAsync commits on its own, so
        // without the transaction a resolved error log could exist with no row naming who resolved
        // it - and the audit row would be a separate write that can fail on its own (planning #533).
        //
        // The transaction runs through ReplaySafeTransaction, so the execution strategy may replay
        // it: a transient failure retries the body rather than failing the request, and the retry
        // writes the audit row once.
        var changed = await _context.ExecuteReplaySafeAsync(async ct =>
        {
            var updated = await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ResolvedAtUtc,
                        command.Resolved ? SystemClock.UtcNow : (DateTime?)null)
                    .SetProperty(e => e.ResolvedByUserId,
                        command.Resolved ? actingId : (Guid?)null)
                    .SetProperty(e => e.ResolvedByUserName,
                        command.Resolved ? actingName : null),
                ct);

            if (updated > 0)
            {
                // ScopeId is required and an error record has none, so the acting user stands in -
                // the same workaround the user and QA-access aggregates already use.
                //
                // The row is tracked, not written: the caller's own save commits it, in the same
                // transaction as the change it describes (planning #533, C-3).
                _eventService.RecordEvent(
                    "ErrorLog",
                    ids.Count == 1 ? ids[0].ToString() : $"bulk:{updated}",
                    command.Resolved ? "ErrorLogsResolved" : "ErrorLogsReopened",
                    null,
                    JsonSerializer.Serialize(new { count = updated, resolved = command.Resolved }),
                    actingId,
                    actingName,
                    $"{(command.Resolved ? "Resolved" : "Reopened")} {updated} error log record(s)",
                    actingId.ToString());

                await _context.SaveChangesAsync(ct);
            }

            return updated;
        }, ct);

        return ResolveErrorLogsResult.Done(changed);
    }
}
