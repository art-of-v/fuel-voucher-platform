namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Settings for the settlement bot: the closed channel the finance team declares arriving money in.
/// </summary>
/// <remarks>
/// <para>
/// A third Telegram bot, deliberately separate from the feedback bot and the alerting bot. Telegram
/// allows exactly one webhook per bot token, so sharing a token would mean routing every update by
/// type at one endpoint — and a flood in a public feedback chat must never be able to silence a
/// stock alert, nor reach the handler that creates payments.
/// </para>
/// <para>
/// <b>The whitelist is the security boundary, not the webhook secret.</b> The secret proves the
/// delivery came from Telegram's API; it proves nothing about who pressed send. Anyone who finds the
/// bot can post to it, so without <see cref="AllowedUserIds"/> a stranger could write
/// <c>1500 ABCD2345</c> and create themselves a payment. That is why the list is empty by default and
/// an empty list rejects everything — see <see cref="IsConfigured"/>.
/// </para>
/// <para>
/// Off by default, like every other opt-in integration here: local dev, tests and CI must never
/// reach the Telegram API.
/// </para>
/// </remarks>
public sealed class TelegramSettlementOptions
{
    public const string SectionName = "Telegram:Settlement";

    /// <summary>
    /// Master switch. When false the webhook answers 200 without persisting anything.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Bot token from @BotFather, for this bot alone. Supply via <c>Telegram__Settlement__BotToken</c>
    /// or user-secrets; never a committed file.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// The secret Telegram echoes on every delivery in <c>X-Telegram-Bot-Api-Secret-Token</c>. Set
    /// once with <c>setWebhook</c>. Compared in constant time.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// The one chat that may declare money. A negative id for a group (e.g. -1001234567890). A group
    /// is the safer shape: membership is controlled by the group itself, so leaving someone does not
    /// need a config change — though <see cref="AllowedUserIds"/> is enforced regardless.
    /// </summary>
    public long AllowedChatId { get; set; }

    /// <summary>
    /// Telegram user ids permitted to declare a payment. Enforced independently of chat membership:
    /// being added to the channel is a Telegram action, and that must not by itself be enough to
    /// create money in the ledger.
    /// </summary>
    public List<long> AllowedUserIds { get; set; } = [];

    /// <summary>
    /// Whether the integration is provisioned. <see cref="AllowedUserIds"/> being non-empty is part
    /// of it, and deliberately so: a bot with a token, a secret and a chat but no whitelist is
    /// provisioned and *wide open*, which is the configuration most likely to be reached by accident
    /// while debugging. Rather than a second flag that someone forgets, the whitelist is simply part
    /// of "configured" — an empty list means nothing is accepted.
    /// </summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(BotToken)
        && !string.IsNullOrWhiteSpace(WebhookSecret)
        && AllowedChatId != 0
        && AllowedUserIds.Count > 0;

    /// <summary>
    /// True when a delivery could be authorised at all. False means the whitelist is empty, which is
    /// treated as "nobody" rather than "everybody" — an absent list must fail closed.
    /// </summary>
    public bool MayAccept(long telegramUserId) =>
        IsConfigured && AllowedUserIds.Contains(telegramUserId);
}