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
    /// </summary>
    Task<long?> SendMessageAsync(long chatId, string text, CancellationToken ct = default);

    /// <summary>
    /// Registers (or refreshes) the webhook. Used by an operator when the public URL changes;
    /// throws on failure so a misconfiguration is loud rather than silent.
    /// </summary>
    Task SetWebhookAsync(string url, CancellationToken ct = default);
}

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

    public async Task<long?> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        if (!_options.IsConfigured || string.IsNullOrWhiteSpace(text))
            return null;

        var body = text.Length > MaxTextLength
            ? string.Concat(text.AsSpan(0, MaxTextLength), "…")
            : text;

        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"/bot{_options.BotToken}/sendMessage",
                new
                {
                    chat_id = chatId,
                    text = body,
                    // No parse_mode: the text contains customer-written content, and HTML/Markdown
                    // in it would either break the send or let a customer style our own messages.
                    disable_web_page_preview = true
                },
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
}