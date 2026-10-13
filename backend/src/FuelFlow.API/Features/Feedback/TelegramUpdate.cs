using System.Text.Json.Serialization;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// The subset of the Telegram Bot API update envelope this feature reads.
/// <para>
/// Deliberately shaped, not mirrored: every property is nullable and nothing is validated beyond
/// what is used. Telegram is a third party whose payload can gain fields, and a webhook that fails
/// to bind because of an unknown key would stop collecting feedback entirely.
/// </para>
/// </summary>
public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("from")]
    public TelegramUser? From { get; set; }

    [JsonPropertyName("chat")]
    public TelegramChat? Chat { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Present when this message quotes another one. This is the entire basis of the
    /// staff-reply relay: a message with no quoted bot post is group chatter and is ignored.</summary>
    [JsonPropertyName("reply_to_message")]
    public TelegramMessage? ReplyToMessage { get; set; }
}

public sealed class TelegramUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Best-effort display name for the audit trail. A bare id is not readable.</summary>
    public string? DisplayName
    {
        get
        {
            var name = $"{FirstName} {LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
            return string.IsNullOrWhiteSpace(Username) ? null : $"@{Username}";
        }
    }
}

public sealed class TelegramChat
{
    /// <summary>"private", "group", "supergroup" or "channel".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public long Id { get; set; }

    public bool IsPrivate => string.Equals(Type, "private", StringComparison.Ordinal);
}