using System.Globalization;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Triages one stored feedback message and, if it carries information, posts it to the staff group.
/// <para>
/// Runs out of band from the webhook because the classifier call is a network round-trip of
/// seconds: Telegram retries an update it did not get a fast 2xx for, and every retry would queue
/// another classification and produce another copy of the customer's message.
/// </para>
/// <para>
/// Three properties this keeps, in order of how much they hurt when lost:
/// </para>
/// <list type="bullet">
/// <item>the message is already in the database - nothing here can lose it;</item>
/// <item>a classifier outage leaves the verdict null and posts nothing, rather than silently
/// dropping or silently promoting a message - an unclassified message is still readable by a
/// human in the admin tab, and the error is recorded on the row;</item>
/// <item>a staff-delivery failure leaves the classified row in place, so re-running the triage is
/// safe and posts exactly once.</item>
/// </list>
/// </summary>
public sealed class FeedbackTriageService
{
    private readonly ApplicationDbContext _context;
    private readonly IFeedbackClassifier _classifier;
    private readonly ITelegramFeedbackClient _bot;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<FeedbackTriageService> _logger;

    public FeedbackTriageService(
        ApplicationDbContext context,
        IFeedbackClassifier classifier,
        ITelegramFeedbackClient bot,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<FeedbackTriageService> logger)
    {
        _context = context;
        _classifier = classifier;
        _bot = bot;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Classifies <paramref name="messageId"/> and delivers it if it survives triage. Safe to run
    /// twice: a row that already reached the staff group exits immediately.
    /// </summary>
    public async Task TriageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var row = await _context.FeedbackMessages
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (row is null)
            return;

        // Already delivered to the staff group: posting twice is how one customer ends up looking
        // like two customers reporting the same bug.
        if (row.StaffMessageId is not null)
            return;

        // No classifier configured means every message is worth reading, so it is posted as-is.
        // This is the deliberate behaviour of a deployment that has not wired an LLM yet: the
        // feedback channel still works, the admin tab is simply full of "привіт".
        if (!_classifier.IsConfigured)
        {
            _logger.LogInformation(
                "No feedback classifier configured; posting message {MessageId} unclassified.", messageId);
            await DeliverAsync(row, null, cancellationToken);
            return;
        }

        FeedbackClassification classification;
        try
        {
            classification = await _classifier.ClassifyAsync(row.Text, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            row.ClassifierError = Truncate(ex.Message, 1000);
            row.ClassifiedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(ex, "Classification failed for feedback message {MessageId}", messageId);

            // Rethrown on purpose. The row is safe and the admin tab shows it as unclassified with
            // the error attached, but it never reaches the staff group - and a message the team
            // cannot see is the one thing this pipeline exists to prevent. Hangfire records the
            // failure on its dashboard (Admin-authenticated), so a retry is one click away instead
            // of a job that silently succeeds and is never looked at.
            throw;
        }

        row.Verdict = classification.Confidence >= _options.MinConfidence
            ? classification.Verdict
            : FeedbackVerdict.Junk;

        // A low-confidence keep is kept in the row's own fields so the admin filter can surface it;
        // only the verdict that drives the staff group is downgraded.
        row.Category = classification.Category;
        row.Severity = classification.Severity;
        row.Summary = classification.Summary;
        row.Language = classification.Language;
        // The classifier's answer is a double - it comes out of a JSON document written by somebody
        // else's model - and this row stores a decimal, because a confidence of 0.6 is a number people
        // read and read back. The conversion goes through the decimal, not the other way round: a
        // double rounded to two places and then converted is a different number from a decimal
        // rounded to two places, and the decimal is the one the column keeps.
        row.Confidence = Math.Round((decimal)classification.Confidence, 2);
        row.ClassifierError = null;
        row.ClassifiedAtUtc = DateTime.UtcNow;

        if (row.Verdict == FeedbackVerdict.Junk)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Dropped feedback message {MessageId} as junk (confidence {Confidence}).",
                messageId, classification.Confidence);
            return;
        }

        await DeliverAsync(row, classification, cancellationToken);
    }

    /// <summary>Posts the message to the staff group and records where it landed, so a reply to
    /// that post can be routed back to the customer.</summary>
    private async Task DeliverAsync(
        FeedbackMessage row,
        FeedbackClassification? classification,
        CancellationToken ct)
    {
        var author = await DescribeAuthorAsync(row.UserId, ct);

        var post = FeedbackBotTexts.StaffPost(
            classification?.Category?.ToString(),
            classification?.Severity?.ToString(),
            author,
            classification?.Summary ?? row.Text,
            row.Text,
            row.AppVersion,
            row.Platform);

        // The one HTML message in the feature: the only tags in it are ours, every interpolated
        // value is escaped by StaffPost. Without parse_mode Telegram ships staff the raw markup.
        var postedId = await _bot.SendMessageAsync(_options.StaffChatId, post, "HTML", ct);
        if (postedId is null)
        {
            _logger.LogWarning(
                "Could not post feedback message {MessageId} to the staff group; it stays in the database.",
                row.Id);
            return;
        }

        row.StaffChatId = _options.StaffChatId;
        row.StaffMessageId = postedId;
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Who to credit in the staff post: the bound account (phone shown in full, because the team
    /// needs to be able to look the person up) or the Telegram handle for an unbound chat.
    /// </summary>
    private async Task<string> DescribeAuthorAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is null)
            return "анонім (Telegram)";

        var phone = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => u.PhoneNumber)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(phone)
            ? "невідомо"
            : string.Create(CultureInfo.InvariantCulture, $"…{phone[^4..]}");
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maxLength)];
}