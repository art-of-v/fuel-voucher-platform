namespace FuelFlow.Features.Feedback.SharedModels;

/// <summary>
/// Which FuelFlow account a customer's private Telegram chat belongs to.
/// <para>
/// A table of its own rather than a column on the messages: a binding is created by <c>/start</c>,
/// which happens BEFORE the customer writes anything, so there is no row to hang it on yet. Reading
/// the owner off "the most recent message in this chat" would leave the first message after every
/// binding unbound - and the first message is usually the one that matters.
/// </para>
/// <para>
/// <c>telegram_chat_id</c> is the primary key: one chat, one account. Re-binding moves it rather
/// than adding a second row, and the inverse index below is what makes "this account now talks to
/// us from a different Telegram" a single UPDATE.
/// </para>
/// </summary>
public sealed class FeedbackChatBinding
{
    /// <summary>Telegram chat id of the customer's private chat.</summary>
    public long TelegramChatId { get; set; }

    public Guid UserId { get; set; }

    public DateTime BoundAtUtc { get; set; }
}