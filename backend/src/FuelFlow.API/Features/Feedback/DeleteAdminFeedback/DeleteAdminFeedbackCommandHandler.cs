using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback.DeleteAdminFeedback;

/// <summary>
/// Deletes messages of the backlog, and their replies with them.
/// </summary>
/// <remarks>
/// This is the "clear the junk" button: the triage step keeps dropped messages so a drifting
/// classifier stays visible, and someone has to be able to act on that.
/// </remarks>
public sealed class DeleteAdminFeedbackCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteAdminFeedbackCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<DeleteAdminFeedbackResult> HandleAsync(
        DeleteAdminFeedbackCommand command,
        CancellationToken ct = default)
    {
        if (command.Id is null && command.Verdict is null)
            return DeleteAdminFeedbackResult.Refused();

        var query = _context.FeedbackMessages.Where(m => true);
        if (command.Id.HasValue)
            query = query.Where(m => m.Id == command.Id.Value);
        if (command.Verdict.HasValue)
            query = query.Where(m => m.Verdict == command.Verdict.Value);

        // Replies first: the FK is Restrict, and the point of a delete is to be able to finish it.
        var messageIds = await query.Select(m => m.Id).ToListAsync(ct);
        await _context.FeedbackMessageReplies.Where(r => messageIds.Contains(r.FeedbackMessageId))
            .ExecuteDeleteAsync(ct);

        var deleted = await query.ExecuteDeleteAsync(ct);

        return DeleteAdminFeedbackResult.Done(deleted);
    }
}
