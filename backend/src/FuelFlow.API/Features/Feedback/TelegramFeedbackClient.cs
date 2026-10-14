using System.Net.Http.Json;
using System.Text.Json;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Sends messages through the feedback bot's own Bot API client. Narrow on purpose: only
/// <c>sendMessage</c> and <c>setWebhook</c>, because those are the only two calls this feature makes.
/// </summary>
public interface ITelegramFeedbackClient
{
    /// <summary>
    /// Delivers a message to a chat and returns its Telegram message id, or null when Telegram
    /// refused it. Never throws for a delivery failure: the caller has already persisted the row,
    /// and an exception here would surface as a 500 on the webhook, which makes Telegram redeliver
    /// an update that was in fact received.
    /// <para>
    /// <paramref name="parseMode"/> is per call, not global, and that is load-bearing. Exactly one
    /// message in this feature is authored as HTML - the staff post, whose every interpolated value
    /// is escaped by <see cref="FeedbackBotTexts.StaffPost"/>, so the only tags in the final string
    /// are ours. Everything else is prose that may legitimately contain <c>&amp;</c> and <c>&lt;</c>:
    /// the welcome texts, the acknowledgement, and above all a staff member's own reply relayed into
    /// a customer chat. Turning HTML on for those would make Telegram reject the send with an opaque
    /// 400 over an ampersand in "Ціна & доставка".
    /// </para>
    /// <para>
    /// A null <paramref name="parseMode"/> means the property is <b>omitted from the JSON</b>, not sent
    /// as null — see the implementation for why that distinction is load-bearing.
    /// </para>
    /// </summary>
    Task<long?> SendMessageAsync(long chatId, string text, string? parseMode = null, CancellationToken ct = default);

    /// <summary>
    /// Registers (or refreshes) the webhook. Used by an operator when the public URL changes;
    /// throws on failure so a misconfiguration is loud rather than silent.
    /// </summary>
    Task SetWebhookAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Telegram's current view of the webhook: where it points, how many updates are waiting, and
    /// the last error. An operator wiring this up needs it - a webhook that silently 401s looks
    /// exactly like a webhook that works.
    /// </summary>
    Task<TelegramWebhookStatus> GetWebhookStatusAsync(CancellationToken ct = default);
}

/// <summary>What Telegram reports about the registered webhook.</summary>
public sealed record TelegramWebhookStatus(string Url, bool HasCustomCertificate, int PendingUpdateCount, string? LastErrorMessage, string? LastErrorDate);

/// <summary>
/// Bot API client for the feedback bot.
/// <para>
/// Separate from <see cref="FuelFlow.SharedKernel.Observability.TelegramAlertNotifier"/> because the
/// two use different tokens: Telegram permits one webhook per token, and an alerting bot whose
/// webhook is consumed by feedback routing would silently stop raising low-stock alerts.
/// </para>
/// </summary>
public sealed class TelegramFeedbackClient : ITelegramFeedbackClient
{
    /// <summary>
    /// Telegram rejects text longer than 4096 characters with an opaque 400, so it is truncated
    /// here rather than surfacing as a delivery failure on an otherwise fine message.
    /// </summary>
    private const int MaxTextLength = 4000;

    private readonly HttpClient _http;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<TelegramFeedbackClient> _logger;

    public TelegramFeedbackClient(
        HttpClient http,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<TelegramFeedbackClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<long?> SendMessageAsync(
        long chatId,
        string text,
        string? parseMode = null,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(text))
            return null;

        var body = text.Length > MaxTextLength
            ? string.Concat(text.AsSpan(0, MaxTextLength), "…")
            : text;

        try
        {
            // Built as a dictionary, not an anonymous object, because `parse_mode` must be **absent**
            // rather than null. System.Text.Json writes a null string property as `"parse_mode": null`,
            // and Telegram answers that with 400 "unsupported parse_mode" — so a plain `parseMode = parseMode`
            // anonymous property silences every message that is *not* the staff post: the welcome, the
            // acknowledgement, a staff reply relayed to a customer. The staff post kept working (its value
            // is a valid "HTML"), which is exactly what made the breakage look like a Telegram problem.
            var payload = new Dictionary<string, object?>
            {
                ["chat_id"] = chatId,
                ["text"] = body,
                ["disable_web_page_preview"] = true
            };

            if (!string.IsNullOrWhiteSpace(parseMode))
            {
                payload["parse_mode"] = parseMode;
            }

            using var response = await _http.PostAsJsonAsync(
                $"/bot{_options.BotToken}/sendMessage",
                payload,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Feedback bot sendMessage to chat {ChatId} failed: {StatusCode} {Body}",
                    chatId, (int)response.StatusCode, error);
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return document.RootElement.TryGetProperty("result", out var result)
                   && result.TryGetProperty("message_id", out var id)
                ? id.GetInt64()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Feedback bot could not deliver a message to chat {ChatId}", chatId);
            return null;
        }
    }

    public async Task SetWebhookAsync(string url, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException(
                "Telegram:Feedback is not configured; the webhook cannot be registered.");

        using var response = await _http.PostAsJsonAsync(
            $"/bot{_options.BotToken}/setWebhook",
            new
            {
                url,
                secret_token = _options.WebhookSecret,
                // Only message updates are interesting: joining a group, a title change and a
                // pinned-message event all cost a webhook delivery and nothing reads them.
                allowed_updates = new[] { "message" },
                drop_pending_updates = false
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"setWebhook failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} — {error}");
        }
    }

    public async Task<TelegramWebhookStatus> GetWebhookStatusAsync(CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return new TelegramWebhookStatus(string.Empty, false, 0, "Telegram:Feedback is not configured", null);

        using var response = await _http.GetAsync($"/bot{_options.BotToken}/getWebhookInfo", ct);
        var payload = await response.Content.ReadAsStringAsync(ct);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        // Telegram answers 200 with {"ok":false,"description":...} for a bot that never had a
        // webhook set, so the envelope - not the status code - is what says whether to read a
        // result. Returning the description as the error is what puts it on the admin screen.
        if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            return new TelegramWebhookStatus(string.Empty, false, 0, description ?? "Telegram returned ok:false", null);
        }

        var result = root.GetProperty("result");

        return new TelegramWebhookStatus(
            ReadString(result, "url") ?? string.Empty,
            ReadBool(result, "has_custom_certificate") ?? false,
            ReadInt(result, "pending_update_count") ?? 0,
            ReadString(result, "last_error_message"),
            ReadString(result, "last_error_date") is { } unix
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(unix)).UtcDateTime.ToString("u")
                : null);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}