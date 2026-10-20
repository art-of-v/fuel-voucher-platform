using System.Security.Claims;
using System.Text.Json;
using FuelFlow.SharedKernel;
using FuelFlow.Persistence;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Features.Brands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// The durable record behind the Telegram staff group: the chat is where the team works, this is
/// where the backlog is searchable, triaged and measurable. Everything the group shows arrives
/// here first, and everything it dropped is still here too.
/// </summary>
[ApiController]
[Route("api/admin/feedback")]
[Authorize(Policy = "Staff")]
public sealed class AdminFeedbackController : ControllerBase
{
    /// <summary>Same reasoning as <c>ErrorLogsController.MaxIdsPerResolveCall</c>: one crafted
    /// request must not become an unbounded write.</summary>
    private const int MaxIdsPerStatusCall = 200;

    private readonly ApplicationDbContext _context;
    private readonly AuditEventService _eventService;

    public AdminFeedbackController(ApplicationDbContext context, AuditEventService eventService)
    {
        _context = context;
        _eventService = eventService;
    }

    /// <summary>Distinct values present in the table, so the admin's filter dropdowns list what
    /// actually exists rather than every enum member the code knows about.</summary>
    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets(CancellationToken ct = default)
    {
        var categories = await _context.FeedbackMessages
            .AsNoTracking()
            .Where(m => m.Category != null)
            .Select(m => m.Category!.Value)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var statuses = await _context.FeedbackMessages
            .AsNoTracking()
            .Select(m => m.Status)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var totals = await _context.FeedbackMessages
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Kept = g.Count(m => m.Verdict == FeedbackVerdict.Kept),
                Junk = g.Count(m => m.Verdict == FeedbackVerdict.Junk),
                Unclassified = g.Count(m => m.Verdict == null),
                Replied = g.Count(m => m.StaffRepliedAtUtc != null)
            })
            .FirstOrDefaultAsync(ct);

        return Ok(new FeedbackFacetsResponse(
            categories,
            statuses,
            totals?.Total ?? 0,
            totals?.Kept ?? 0,
            totals?.Junk ?? 0,
            totals?.Unclassified ?? 0,
            totals?.Replied ?? 0));
    }

    /// <summary>
    /// The list. Defaults to kept messages only: the whole point of the triage step is that the
    /// team reads fewer, better rows, and a default of "everything" would put "йоук" back at the
    /// top of the page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? verdict = null,
        [FromQuery] string? category = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] decimal? maxConfidence = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = _context.FeedbackMessages.AsNoTracking();

        if (string.IsNullOrWhiteSpace(verdict))
        {
            query = query.Where(m => m.Verdict == FeedbackVerdict.Kept);
        }
        else if (Enum.TryParse<FeedbackVerdict>(verdict, ignoreCase: true, out var parsedVerdict))
        {
            query = query.Where(m => m.Verdict == parsedVerdict);
        }
        else
        {
            // An unrecognised verdict must not widen the filter to "no filter" and dump the whole
            // table at a caller who asked for something narrower.
            query = query.Where(m => m.Verdict == FeedbackVerdict.Kept);
        }

        if (!string.IsNullOrWhiteSpace(category)
            && Enum.TryParse<FeedbackCategory>(category, ignoreCase: true, out var parsedCategory))
        {
            query = query.Where(m => m.Category == parsedCategory);
        }

        if (!string.IsNullOrWhiteSpace(severity)
            && Enum.TryParse<FeedbackSeverity>(severity, ignoreCase: true, out var parsedSeverity))
        {
            query = query.Where(m => m.Severity == parsedSeverity);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<FeedbackStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(m => m.Status == parsedStatus);
        }

        // The drift check: junk the classifier was not sure about. A rising count here is the
        // early warning that it is dropping real complaints.
        if (maxConfidence.HasValue)
            query = query.Where(m => m.Verdict == FeedbackVerdict.Junk && m.Confidence <= maxConfidence.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m =>
                m.Text.Contains(search)
                || (m.Summary != null && m.Summary.Contains(search)));
        }

        if (from.HasValue)
            query = query.Where(m => m.CreatedAtUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(m => m.CreatedAtUtc <= to.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip(PageLimits.ClampOffset(offset))
            .Take(PageLimits.ClampPageSize(limit))
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

        return Ok(new FeedbackListResponse(total, items));
    }

    /// <summary>One message plus every answer the team gave it.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var item = await _context.FeedbackMessages
            .AsNoTracking()
            .Where(m => m.Id == id)
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
            return NotFound();

        var replies = await _context.FeedbackMessageReplies
            .AsNoTracking()
            .Where(r => r.FeedbackMessageId == id)
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

        return Ok(new FeedbackDetailResponse(item, replies));
    }

    /// <summary>Moves rows between review states. Re-posting an id that is already in the
    /// requested state is a no-op rather than an error, so a double-click cannot rewrite who
    /// handled a message.</summary>
    [HttpPost("status")]
    public async Task<IActionResult> UpdateStatus(
        [FromBody] UpdateFeedbackStatusRequest request,
        CancellationToken ct = default)
    {
        if (request.Ids is not { Count: > 0 })
            return BadRequest(new { error = "No feedback ids supplied" });

        if (!Enum.TryParse<FeedbackStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = $"Unknown status '{request.Status}'." });

        var actingId = CurrentUserId();
        if (actingId is null)
            return Unauthorized();

        var ids = request.Ids.Distinct().Take(MaxIdsPerStatusCall).ToList();

        // ExecuteUpdateAsync bypasses change tracking by design, so it lands regardless of the
        // context's default query behaviour (DatabaseSetup).
        var changed = await _context.FeedbackMessages
            .Where(m => ids.Contains(m.Id) && m.Status != status)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.Status, status),
                ct);

        if (changed > 0)
        {
            await _eventService.RecordEventAsync(
                "FeedbackMessage",
                ids.Count == 1 ? ids[0].ToString() : $"bulk:{changed}",
                "FeedbackStatusChanged",
                null,
                JsonSerializer.Serialize(new { count = changed, status = status.ToString() }),
                actingId.Value,
                CurrentUserName(),
                $"Marked {changed} feedback message(s) as {status}",
                actingId.Value.ToString(),
                ct);
        }

        return Ok(new FeedbackMutationResponse(true, changed));
    }

    /// <summary>
    /// Deletes rows outright. This is the "clear the junk" button: the triage step keeps dropped
    /// messages so a drifting classifier stays visible, and someone has to be able to act on that.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(
        [FromQuery] Guid? id = null,
        [FromQuery] FeedbackVerdict? verdict = null,
        CancellationToken ct = default)
    {
        if (id is null && verdict is null)
            return BadRequest(new { error = "Supply either id or verdict." });

        var query = _context.FeedbackMessages.Where(m => true);
        if (id.HasValue)
            query = query.Where(m => m.Id == id.Value);
        if (verdict.HasValue)
            query = query.Where(m => m.Verdict == verdict.Value);

        // Replies first: the FK is Restrict, and the point of a delete is to be able to finish it.
        var messageIds = await query.Select(m => m.Id).ToListAsync(ct);
        await _context.FeedbackMessageReplies.Where(r => messageIds.Contains(r.FeedbackMessageId))
            .ExecuteDeleteAsync(ct);

        var deleted = await query.ExecuteDeleteAsync(ct);
        return Ok(new FeedbackMutationResponse(true, deleted));
    }

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }

    private string? CurrentUserName() =>
        User.FindFirst(ClaimTypes.Name)?.Value
        ?? User.FindFirst("first_name")?.Value;
}

public sealed record AdminFeedbackDto
{
    public Guid Id { get; init; }
    public Guid? UserId { get; init; }
    public long TelegramChatId { get; init; }
    public string Text { get; init; } = null!;
    public FeedbackVerdict? Verdict { get; init; }
    public FeedbackCategory? Category { get; init; }
    public FeedbackSeverity? Severity { get; init; }
    public string? Summary { get; init; }
    public FeedbackLanguage? Language { get; init; }
    public decimal? Confidence { get; init; }
    public string? ClassifierError { get; init; }
    public DateTime? ClassifiedAtUtc { get; init; }
    public FeedbackStatus Status { get; init; }
    public long? StaffMessageId { get; init; }
    public DateTime? StaffRepliedAtUtc { get; init; }
    public string? AppVersion { get; init; }
    public string? Platform { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int ReplyCount { get; set; }
}

public sealed record FeedbackReplyDto
{
    public Guid Id { get; init; }
    public string? StaffUserName { get; init; }
    public string Text { get; init; } = null!;
    public long? DeliveredMessageId { get; init; }
    public string? DeliveryError { get; init; }
    public DateTime SentAtUtc { get; init; }
}

public sealed record UpdateFeedbackStatusRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public string Status { get; init; } = nameof(FeedbackStatus.Handled);
}

/// <summary>Named rather than anonymous: the admin client and the tests bind to these, so a rename
/// on the server breaks the build instead of silently producing nulls in both.</summary>
public sealed record FeedbackListResponse(int Total, IReadOnlyList<AdminFeedbackDto> Items);

public sealed record FeedbackDetailResponse(AdminFeedbackDto Item, IReadOnlyList<FeedbackReplyDto> Replies);

public sealed record FeedbackFacetsResponse(
    IReadOnlyList<FeedbackCategory> Categories,
    IReadOnlyList<FeedbackStatus> Statuses,
    int Total,
    int Kept,
    int Junk,
    int Unclassified,
    int Replied);

public sealed record FeedbackMutationResponse(bool Success, int Affected);