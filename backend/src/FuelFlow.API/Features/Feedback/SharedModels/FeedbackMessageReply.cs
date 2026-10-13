namespace FuelFlow.Features.Feedback.SharedModels;

/// <summary>
/// One answer a team member sent back to a customer from the staff group, recorded so the
/// conversation is auditable after the fact. Delivery failure is recorded on the row rather than
/// thrown away: an answer the customer never received is still something the team needs to see.
/// </summary>
public sealed class FeedbackMessageReply
{
    public Guid Id { get; set; }

    public Guid FeedbackMessageId { get; set; }

    public FeedbackMessage? FeedbackMessage { get; set; }

    /// <summary>Telegram id of the staff member who wrote it.</summary>
    public long StaffUserId { get; set; }

    /// <summary>Display name captured at send time - Telegram ids are not a readable audit trail.</summary>
    public string? StaffUserName { get; set; }

    public string Text { get; set; } = null!;

    /// <summary>Id the reply got in the customer's chat, or null when delivery failed.</summary>
    public long? DeliveredMessageId { get; set; }

    /// <summary>Last delivery failure reason, truncated. Null on success.</summary>
    public string? DeliveryError { get; set; }

    public DateTime SentAtUtc { get; set; }
}