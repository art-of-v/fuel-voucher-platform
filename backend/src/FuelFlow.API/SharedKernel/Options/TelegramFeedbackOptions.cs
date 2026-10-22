namespace FuelFlow.SharedKernel.Options;

/// <summary>
/// Settings for the in-app feedback Telegram bot: a private 1-on-1 chat per customer, an
/// LLM that decides which messages carry information, and a private staff group that receives
/// only the useful ones.
/// <para>
/// Deliberately separate from <see cref="TelegramOptions"/> (the infrastructure alerting bot).
/// Telegram allows exactly one webhook per bot token, so the two concerns cannot share a token
/// without routing every update by type at one endpoint - and a misconfigured webhook, a revoked
/// token or a flood in the feedback chat must never be able to silence a low-stock alert.
/// </para>
/// <para>
/// Off by default, like every other opt-in integration here: local dev, tests and CI must never
/// reach the Telegram API.
/// </para>
/// </summary>
public sealed class TelegramFeedbackOptions
{
    public const string SectionName = "Telegram:Feedback";

    /// <summary>
    /// Master switch. When false the webhook answers 200 without persisting anything, the
    /// deep-link endpoint reports the feature as unavailable, and the classification job no-ops.
    /// Users already inside the bot chat get the "temporarily unavailable, write to
    /// support@palne.shop" message instead of silence.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Bot token from @BotFather for the feedback bot (not the alerting bot).
    /// Supply via <c>Telegram__Feedback__BotToken</c> or user-secrets; never a committed file.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// The secret Telegram echoes back on every webhook delivery in
    /// <c>X-Telegram-Bot-Api-Secret-Token</c>. Set once with <c>setWebhook</c>; the value must be
    /// 1-16 characters of <c>A-Za-z0-9_</c> and <c>-</c>. Compared in constant time - it is the
    /// only thing standing between a public endpoint and anyone who guesses its URL.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Bot username without the leading @. Used to build the deep link handed to the mobile app
    /// (<c>https://t.me/{username}?start={payload}</c>), so the app never constructs it itself.
    /// </summary>
    public string BotUsername { get; set; } = string.Empty;

    /// <summary>
    /// The private group that receives triage-passing messages (negative id, e.g. -1001234567890).
    /// Must be a group the bot is an administrator of, otherwise Telegram's privacy mode hides
    /// staff replies from it and the two-way half of the feature is silently dead.
    /// </summary>
    public long StaffChatId { get; set; }

    /// <summary>
    /// Telegram user ids allowed to relay an answer to a customer. Enforced independently of group
    /// membership: being added to the group is a Telegram action, and that must not be enough to
    /// start messaging customers.
    /// </summary>
    public List<long> StaffUserIds { get; set; } = [];

    /// <summary>
    /// HMAC key for the <c>/start</c> deep-link payload that binds a Telegram chat to a FuelFlow
    /// account. Rotating it invalidates every outstanding deep link, which is the intended
    /// emergency action if a link leaks - the affected users simply start an unbound chat.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long a signed deep link stays usable. Past this the payload is treated as junk.</summary>
    public int DeepLinkTtlDays { get; set; } = 30;

    /// <summary>
    /// How many messages one customer chat may send per hour. A person reporting three problems
    /// writes three messages; a loop hitting the endpoint writes hundreds, and each one costs a
    /// classifier call and an entry in the staff group.
    /// </summary>
    public int MinMessagesPerHour { get; set; } = 10;

    /// <summary>Longest message accepted from the bot chat. Telegram allows 4096; we keep 2000
    /// because anything longer is a paste, not feedback.</summary>
    public int MaxMessageLength { get; set; } = 2000;

    /// <summary>
    /// A classifier verdict below this confidence is stored as <c>junk</c> but keeps its low
    /// confidence, so the admin can filter "dropped, and we are not sure" and check for a classifier
    /// that has started eating real complaints.
    /// </summary>
    public double MinConfidence { get; set; } = 0.6;

    public string BaseUrl { get; set; } = "https://api.telegram.org";

    /// <summary>The deadline Telegram is given on the feedback path.</summary>
    public const int DefaultTimeoutSeconds = 10;

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Whether the integration is provisioned: a token, the webhook secret, a signing key and a
    /// staff chat to deliver to. Deliberately does NOT include <see cref="Enabled"/>.
    /// <para>
    /// The two answer different questions. Provisioned-but-off means "the bot exists, the admin
    /// turned the channel off" - and in that state the customer who is already inside the chat must
    /// still be told to write to the web form, which is exactly when a client that refuses to send
    /// would leave them talking to silence. Unprovisioned means there is no bot at all, and nothing
    /// is sent anywhere.
    /// </para>
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BotToken)
        && !string.IsNullOrWhiteSpace(WebhookSecret)
        && !string.IsNullOrWhiteSpace(SigningKey)
        && StaffChatId != 0;
}