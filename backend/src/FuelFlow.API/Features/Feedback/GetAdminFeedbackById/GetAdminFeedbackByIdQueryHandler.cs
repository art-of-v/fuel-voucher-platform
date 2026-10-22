using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback.GetAdminFeedbackById;

/// <summary>
/// Reads one message of the backlog together with every answer the team gave it.
/// </summary>
/// <remarks>
/// The replies come in the order they were sent, because the conversation is the point of them:
/// an answer that arrives before the question it answers is a different thing.
/// </remarks>
public sealed class GetAdminFeedbackByIdQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminFeedbackByIdQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<FeedbackDetailResponse?> HandleAsync(
        GetAdminFeedbackByIdQuery query,
        CancellationToken ct = default)
    {
        var item = await _context.FeedbackMessages
            .AsNoTracking()
            .Where(m => m.Id == query.Id)
            .Select(m => new AdminFeedbackDto
            {
                Id = m.Id,
                UserId = m.UserId,
                TelegramChatId = m.TelegramChatId,
                Text = m.Text,
                Verdict = m.Verdict,
                Category = m.Category,
                Severity = m.Severity,
                Summary = m.Summary,
                Language = m.Language,
                Confidence = m.Confidence,
                ClassifierError = m.ClassifierError,
                ClassifiedAtUtc = m.ClassifiedAtUtc,
                Status = m.Status,
                StaffMessageId = m.StaffMessageId,
                StaffRepliedAtUtc = m.StaffRepliedAtUtc,
                AppVersion = m.AppVersion,
                Platform = m.Platform,
                CreatedAtUtc = m.CreatedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return null;

        var replies = await _context.FeedbackMessageReplies
            .AsNoTracking()
            .Where(r => r.FeedbackMessageId == query.Id)
            .OrderBy(r => r.SentAtUtc)
            .Select(r => new FeedbackReplyDto
            {
                Id = r.Id,
                StaffUserName = r.StaffUserName,
                Text = r.Text,
                DeliveredMessageId = r.DeliveredMessageId,
                DeliveryError = r.DeliveryError,
                SentAtUtc = r.SentAtUtc
            })
            .ToListAsync(ct);

        return new FeedbackDetailResponse(item, replies);
    }
}
