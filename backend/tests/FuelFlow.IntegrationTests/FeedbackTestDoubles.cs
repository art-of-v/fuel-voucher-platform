using System.Linq.Expressions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Feedback;
using FuelFlow.Features.Feedback.SharedModels;
using FuelFlow.SharedKernel.Options;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Records what the pipeline tried to send instead of calling Telegram. Every test that touches
/// delivery asserts on this: the two-way path's whole promise is "only a reply to a bot post,
/// only from an allowlisted id", and that is a statement about outbound calls.
/// </summary>
public sealed class RecordingTelegramFeedbackClient : ITelegramFeedbackClient
{
    /// <summary>
    /// Message ids are drawn from a shared counter rather than a per-client constant. The staff
    /// reply relay routes on (staff_chat_id, staff_message_id) - both fixed values in these tests -
    /// so two clients handing out the same id would send one test's staff reply into another test's
    /// customer chat, and only the whole-class run would catch it.
    /// </summary>
    private static long _sharedMessageId = 900_000;

    private long _nextMessageId = Interlocked.Add(ref _sharedMessageId, 1_000);

    public List<(long ChatId, string Text)> Sent { get; } = [];

    /// <summary>Chat id whose delivery fails, so the "stored but undelivered" paths are reachable.</summary>
    public long? FailForChatId { get; set; }

    /// <summary>What SendMessageAsync hands back. Distinct from the chat id so a reply can be
    /// traced to the message it answered.</summary>
    public long NextMessageId
    {
        get => _nextMessageId;
        set => _nextMessageId = value;
    }

    public Task<long?> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        Sent.Add((chatId, text));

        if (FailForChatId == chatId)
            return Task.FromResult<long?>(null);

        return Task.FromResult<long?>(Interlocked.Increment(ref _nextMessageId));
    }

    public Task SetWebhookAsync(string url, CancellationToken ct = default) => Task.CompletedTask;

    public IEnumerable<string> SentTo(long chatId) =>
        Sent.Where(s => s.ChatId == chatId).Select(s => s.Text);
}

/// <summary>
/// A classifier whose verdict is chosen by the test. The point of the pipeline is the routing
/// around the model, so the model itself is a stub and its parsing is covered by unit tests.
/// </summary>
public sealed class StubFeedbackClassifier : IFeedbackClassifier
{
    public bool IsConfigured { get; set; } = true;

    public FeedbackClassification Result { get; set; } = new(
        FeedbackVerdict.Kept,
        FeedbackCategory.Idea,
        FeedbackSeverity.Low,
        "summary",
        FeedbackLanguage.Uk,
        0.9);

    public Exception? Throw { get; set; }

    public List<string> Seen { get; } = [];

    public Task<FeedbackClassification> ClassifyAsync(string text, CancellationToken ct = default)
    {
        Seen.Add(text);

        return Throw is not null
            ? Task.FromException<FeedbackClassification>(Throw)
            : Task.FromResult(Result);
    }
}

public static class FeedbackTestFactory
{
    public const string SigningKey = "test-signing-key-long-enough";
    public const string WebhookSecret = "fbhook";
    public const long StaffChatId = -1001234567890;
    public const long StaffUserId = 555;
    public const long OutsiderUserId = 999;

    public static TelegramFeedbackOptions Options(Action<TelegramFeedbackOptions>? configure = null)
    {
        var options = new TelegramFeedbackOptions
        {
            Enabled = true,
            BotToken = "test-token",
            WebhookSecret = WebhookSecret,
            BotUsername = "palne_feedback_bot",
            StaffChatId = StaffChatId,
            StaffUserIds = [StaffUserId],
            SigningKey = SigningKey
        };

        configure?.Invoke(options);
        return options;
    }

    public static FeedbackUpdateRouter Router(
        FuelFlow.Persistence.ApplicationDbContext context,
        ITelegramFeedbackClient client,
        IBackgroundJobClient jobs,
        TelegramFeedbackOptions? options = null) =>
        new(
            context,
            client,
            jobs,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            NullLogger<FeedbackUpdateRouter>.Instance);

    public static FeedbackTriageService Triage(
        FuelFlow.Persistence.ApplicationDbContext context,
        IFeedbackClassifier classifier,
        ITelegramFeedbackClient client,
        TelegramFeedbackOptions? options = null) =>
        new(
            context,
            classifier,
            client,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            NullLogger<FeedbackTriageService>.Instance);

    public static TelegramUpdate CustomerMessage(
        long chatId,
        long messageId,
        string text,
        long fromId = 42,
        long? replyToMessageId = null,
        long? replyToChatId = null,
        bool botQuoted = false) =>
        new()
        {
            UpdateId = messageId * 100 + 7,
            Message = new TelegramMessage
            {
                MessageId = messageId,
                Text = text,
                From = new TelegramUser { Id = fromId, FirstName = "Test" },
                Chat = new TelegramChat { Id = chatId, Type = replyToChatId is null ? "private" : "supergroup" },
                ReplyToMessage = replyToMessageId is null
                    ? null
                    : new TelegramMessage
                    {
                        MessageId = replyToMessageId.Value,
                        From = botQuoted
                            ? new TelegramUser { Id = 1, IsBot = true, FirstName = "Bot" }
                            : new TelegramUser { Id = OutsiderUserId, FirstName = "Someone" }
                    }
            }
        };

    public static TelegramUpdate StartCommand(long chatId, string? payload) =>
        CustomerMessage(chatId, messageId: 1, text: payload is null ? "/start" : $"/start {payload}");
}