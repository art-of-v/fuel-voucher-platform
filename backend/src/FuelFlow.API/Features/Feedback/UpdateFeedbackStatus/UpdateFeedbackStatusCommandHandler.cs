using System.Text.Json;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback.UpdateFeedbackStatus;

/// <summary>
/// Moves feedback messages between review states, and records who decided it.
/// </summary>
/// <remarks>
/// <para>
/// Re-posting an id that is already in the requested state is a no-op rather than an error, so a
/// double-click cannot rewrite who handled a message - and the audit log records decisions, not
/// clicks, so the second click adds no second audit row.
/// </para>
/// <para>
/// The status change and the audit row are ONE transaction. The bulk update commits on its own,
/// so without the transaction a message could move states with no row naming who moved it - and
/// the row would be a separate write that can fail on its own (planning #533).
/// </para>
/// </remarks>
public sealed class UpdateFeedbackStatusCommandHandler
{
    /// <summary>Same reasoning as <c>ResolveErrorLogsCommandHandler.MaxIdsPerResolveCall</c>: one crafted
    /// request must not become an unbounded write.</summary>
    private const int MaxIdsPerStatusCall = 200;

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public UpdateFeedbackStatusCommandHandler(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    public async Task<UpdateFeedbackStatusResult> HandleAsync(
        UpdateFeedbackStatusCommand command,
        CancellationToken ct = default)
    {
        if (command.Ids is not { Count: > 0 })
            return UpdateFeedbackStatusResult.NoIds();

        if (!Enum.TryParse<FeedbackStatus>(command.Status, ignoreCase: true, out var status))
            return UpdateFeedbackStatusResult.UnknownStatus(command.Status);

        if (command.ActingUserId is null)
            return UpdateFeedbackStatusResult.Unauthenticated();

        var actingId = command.ActingUserId.Value;
        var ids = command.Ids.Distinct().Take(MaxIdsPerStatusCall).ToList();

        // ExecuteUpdateAsync bypasses change tracking by design, so it lands regardless of the
        // context's default query behaviour (DatabaseSetup).
        //
        // One transaction for the status change and the audit row. ExecuteUpdateAsync commits on its
        // own, so without the transaction a message could move states with no row naming who moved
        // it - and the audit row would be a separate write that can fail on its own (planning #533).
        await using var tx = _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(ct)
            : null;

        var changed = await _context.FeedbackMessages
            .Where(m => ids.Contains(m.Id) && m.Status != status)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.Status, status),
                ct);

        if (changed > 0)
        {
            // The row is tracked, not written: the caller's own save commits it, in the same
            // transaction as the change it describes (planning #533, C-3).
            _eventService.RecordEvent(
                "FeedbackMessage",
                ids.Count == 1 ? ids[0].ToString() : $"bulk:{changed}",
                "FeedbackStatusChanged",
                null,
                JsonSerializer.Serialize(new { count = changed, status = status.ToString() }),
                actingId,
                command.ActingUserName,
                $"Marked {changed} feedback message(s) as {status}",
                actingId.ToString());

            await _context.SaveChangesAsync(ct);
        }

        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return UpdateFeedbackStatusResult.Done(changed);
    }
}
