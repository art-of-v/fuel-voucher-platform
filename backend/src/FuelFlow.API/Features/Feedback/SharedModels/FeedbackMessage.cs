namespace FuelFlow.Features.Feedback.SharedModels;

/// <summary>
/// One message a customer sent to the feedback bot in their private 1-on-1 Telegram chat.
/// <para>
/// Written the moment the webhook lands, before any classification: a classifier outage or a
/// Telegram delivery failure must never be the reason a customer's complaint disappears, and the
/// row is also what makes the incoming webhook idempotent - Telegram re-delivers on any non-2xx,
/// so <c>(TelegramChatId, TelegramMessageId)</c> is uniquely indexed and a re-delivery is a no-op.
/// </para>
/// <para>
/// <see cref="UserId"/> is nullable on purpose. The deep link binds a chat to an account, but anyone
/// can find the bot by name and start a chat without it; refusing those messages would throw away
/// exactly the feedback from people who could not work out how to reach us.
/// </para>
/// </summary>
public sealed class FeedbackMessage
{
    public Guid Id { get; set; }

    /// <summary>The FuelFlow account this chat was bound to, or null for an unbound chat.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Telegram chat id of the customer's private chat (positive for a 1-on-1 chat).</summary>
    public long TelegramChatId { get; set; }

    /// <summary>Id of this message inside that chat. Unique per chat - the webhook de-dupe key.</summary>
    public long TelegramMessageId { get; set; }

    /// <summary>What the customer wrote, capped at the configured maximum length.</summary>
    public string Text { get; set; } = null!;

    /// <summary>Null until the classifier has run. Null means "not classified", not "junk".</summary>
    public FeedbackVerdict? Verdict { get; set; }

    public FeedbackCategory? Category { get; set; }

    public FeedbackSeverity? Severity { get; set; }

    /// <summary>One-sentence restatement written by the classifier for the staff chat. Never invented.</summary>
    public string? Summary { get; set; }

    public FeedbackLanguage? Language { get; set; }

    /// <summary>Classifier confidence 0..1. Retained even for dropped messages, so a drifting
    /// classifier is visible in the admin filter rather than invisible in the discard bin.</summary>
    /// <remarks>
    /// A decimal, not a double. The classifier's own answer is a double - it comes out of a JSON
    /// document written by somebody else's model - but the number this system stores and compares
    /// against a threshold is a decimal, because binary floating point cannot represent 0.1 exactly
    /// and a confidence of 0.6 is a number people read.
    /// </remarks>
    public decimal? Confidence { get; set; }

    /// <summary>
    /// Last classifier failure, truncated. A row with this set and no verdict is one the team can
    /// still read - it is only the automation that failed.
    /// </summary>
    public string? ClassifierError { get; set; }

    /// <summary>When the classifier last ran, so a failed row can be retried or aged out.</summary>
    public DateTime? ClassifiedAtUtc { get; set; }

    /// <summary>Where this message was posted for the team, and the id it got there under. A staff
    /// reply is only relayed when it quotes this id, which is what stops group chatter reaching customers.</summary>
    public long? StaffChatId { get; set; }

    public long? StaffMessageId { get; set; }

    /// <summary>Set once an allowlisted team member's reply was delivered to the customer.</summary>
    public DateTime? StaffRepliedAtUtc { get; set; }

    public FeedbackStatus Status { get; set; } = FeedbackStatus.New;

    /// <summary>Client build the message came from - the first thing asked for in a bug report.</summary>
    public string? AppVersion { get; set; }

    /// <summary>Client platform (ios / android / web).</summary>
    public string? Platform { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}