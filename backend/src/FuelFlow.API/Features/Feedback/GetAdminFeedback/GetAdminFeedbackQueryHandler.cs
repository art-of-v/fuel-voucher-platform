using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback.GetAdminFeedback;

/// <summary>
/// Reads the feedback backlog: the messages, filtered and paged.
/// </summary>
/// <remarks>
/// <para>
/// The list opens on the kept messages. A default of "everything" would put "йоук" back at the
/// top of the page, and the triage step would be doing nothing for the person reading it.
/// </para>
/// <para>
/// Every filter narrows, and an unrecognised value does not widen: a crafted query string must
/// not turn a narrow request into a dump of the table.
/// </para>
/// </remarks>
public sealed class GetAdminFeedbackQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetAdminFeedbackQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<FeedbackListResponse> HandleAsync(
        GetAdminFeedbackQuery query,
        CancellationToken ct = default)
    {
        var messages = _context.FeedbackMessages.AsNoTracking();

        // The filters are copied into locals first: the compiler keeps a local's "not null" state
        // inside a lambda, and a property's it does not - so the copies are what keeps the
        // Contains(...) calls below free of nullability warnings.
        var verdict = query.Verdict;
        var category = query.Category;
        var severity = query.Severity;
        var status = query.Status;
        var search = query.Search;
        var maxConfidence = query.MaxConfidence;
        var from = query.From;
        var to = query.To;

        if (string.IsNullOrWhiteSpace(verdict))
        {
            messages = messages.Where(m => m.Verdict == FeedbackVerdict.Kept);
        }
        else if (Enum.TryParse<FeedbackVerdict>(verdict, ignoreCase: true, out var parsedVerdict))
        {
            messages = messages.Where(m => m.Verdict == parsedVerdict);
        }
        else
        {
            // An unrecognised verdict must not widen the filter to "no filter" and dump the whole
            // table at a caller who asked for something narrower.
            messages = messages.Where(m => m.Verdict == FeedbackVerdict.Kept);
        }

        if (!string.IsNullOrWhiteSpace(category)
            && Enum.TryParse<FeedbackCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            messages = messages.Where(m => m.Category == parsedCategory);
        }

        if (!string.IsNullOrWhiteSpace(severity)
            && Enum.TryParse<FeedbackSeverity>(severity, ignoreCase: true, out var parsedSeverity))
        {
            messages = messages.Where(m => m.Severity == parsedSeverity);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<FeedbackStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            messages = messages.Where(m => m.Status == parsedStatus);
        }

        // The drift check: junk the classifier was not sure about. A rising count here is the
        // early warning that it is dropping real complaints.
        if (maxConfidence.HasValue)
            messages = messages.Where(m => m.Verdict == FeedbackVerdict.Junk && m.Confidence <= maxConfidence.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            messages = messages.Where(m =>
                m.Text.Contains(search)
                || (m.Summary != null && m.Summary.Contains(search)));
        }

        if (from.HasValue)
            messages = messages.Where(m => m.CreatedAtUtc >= from.Value);
        if (to.HasValue)
            messages = messages.Where(m => m.CreatedAtUtc <= to.Value);

        var total = await messages.CountAsync(ct);

        var items = await messages
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip(PageLimits.ClampOffset(query.Offset))
            .Take(PageLimits.ClampPageSize(query.Limit))
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
                StaffRepliedAtUtc = m.StaffRepliedAtUtc,
                AppVersion = m.AppVersion,
                Platform = m.Platform,
                CreatedAtUtc = m.CreatedAtUtc
            })
            .ToListAsync(ct);

        // Reply counts as a second, grouped query rather than a correlated subquery inside the
        // projection: the list is paged, so only the ids actually on this page are counted, and a
        // per-row COUNT in the projection is a query the planner handles badly at this size.
        var pageIds = items.Select(i => i.Id).ToList();
        var replyCounts = await _context.FeedbackMessageReplies
            .AsNoTracking()
            .Where(r => pageIds.Contains(r.FeedbackMessageId))
            .GroupBy(r => r.FeedbackMessageId)
            .Select(g => new { MessageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MessageId, x => x.Count, ct);

        foreach (var item in items)
        {
            item.ReplyCount = replyCounts.GetValueOrDefault(item.Id);
        }

        return new FeedbackListResponse(total, items);
    }
}
