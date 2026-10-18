using FuelFlow.Payments.SharedModels;

namespace FuelFlow.Features.Settlement.SharedModels;

/// <summary>
/// What became of one Telegram message. Every outcome is stored, including the ones that produced
/// no payment.
/// </summary>
/// <remarks>
/// The refusals are recorded rather than merely logged. "Someone tried to post a settlement and was
/// rejected" is the question an auditor asks first about a channel that moves money, and a log line
/// rotates away; the message id, who sent it and why it was refused are the evidence. It is also the
/// only way to notice a sender who is being refused repeatedly, which is what an attempt to use this
/// channel for something else looks like.
/// </remarks>
public static class SettlementOutcomes
{
    /// <summary>Parsed, and the code resolved to a customer. A payment exists and may be attached.</summary>
    public const string Accepted = "accepted";

    /// <summary>
    /// Parsed, but the money could not be placed: the code matched nobody, several orders matched, or
    /// the amount did not agree with the one order that did. The money is real, so it is recorded and
    /// left for an operator - never guessed at.
    /// </summary>
    public const string Unallocated = "unallocated";

    /// <summary>The message did not match the one accepted shape.</summary>
    public const string Unparseable = "unparseable";

    /// <summary>The sender is not on the allow list. No payment, and the attempt is recorded.</summary>
    public const string RejectedSender = "rejected_sender";

    /// <summary>The message came from a chat this bot does not serve.</summary>
    public const string RejectedChat = "rejected_chat";

    public static readonly string[] All =
        [Accepted, Unallocated, Unparseable, RejectedSender, RejectedChat];

    public static bool IsKnown(string? value) =>
        value is Accepted or Unallocated or Unparseable or RejectedSender or RejectedChat;
}

/// <summary>
/// One message received on the settlement channel, kept whether or not it produced a payment.
/// </summary>
/// <remarks>
/// <para>
/// This table is the idempotency key and the audit trail in one. <c>(chat_id, message_id)</c> is
/// unique, so Telegram redelivering an update — which it does on any non-2xx, and does retry — cannot
/// create a second payment for the same message. That matters more here than anywhere else: a
/// duplicate is not a duplicate message, it is money twice.
/// </para>
/// <para>
/// The raw text is kept rather than a hash of it. The text is the evidence — "why did 2000 become
/// this customer's payment" is answered by reading what was written, and a hash answers nothing a
/// human can use. It is short by construction: the parser accepts one line.
/// </para>
/// </remarks>
public sealed class SettlementMessage
{
    public Guid Id { get; set; }

    /// <summary>Telegram's chat id. Part of the idempotency key, because message ids are per-chat.</summary>
    public long ChatId { get; set; }

    /// <summary>Telegram's per-chat message id. The idempotency key; see the remarks above.</summary>
    public long MessageId { get; set; }

    /// <summary>
    /// Who sent it. Kept even for a refused message: the refusal is only meaningful with the sender,
    /// and an authorisation row that cannot name who was refused names nothing.
    /// </summary>
    public long TelegramUserId { get; set; }

    /// <summary>The display name at the time, so the record survives the person renaming themselves.</summary>
    public string? TelegramDisplayName { get; set; }

    /// <summary>The message as written, trimmed. Null only for a message that carried no text at all.</summary>
    public string? Text { get; set; }

    /// <summary>Telegram's own timestamp for the message, not our receive time.</summary>
    public DateTime SentAtUtc { get; set; }

    public string Outcome { get; set; } = SettlementOutcomes.Unparseable;

    /// <summary>Why, when the outcome is not <see cref="SettlementOutcomes.Accepted"/>.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>What the parser read, kept even when nothing matched — see <see cref="Text"/>.</summary>
    public decimal? AmountUah { get; set; }

    /// <summary>The code as read, upper-cased. Kept for the same reason as the amount.</summary>
    public string? Code { get; set; }

    /// <summary>The customer the code resolved to, when it resolved to one.</summary>
    public Guid? ResolvedUserId { get; set; }

    /// <summary>The payment this produced, when it produced one.</summary>
    public Guid? PaymentId { get; set; }

    public Payment? Payment { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}