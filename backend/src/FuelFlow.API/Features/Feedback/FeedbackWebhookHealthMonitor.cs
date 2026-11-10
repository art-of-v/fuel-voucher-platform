using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Asks Telegram whether the feedback bot still has a webhook, and publishes the answer as
/// <c>fuelflow.telegram.feedback_webhook_missing</c>.
/// </summary>
/// <remarks>
/// <para>
/// The webhook is registered by hand, once, and nothing re-runs it: not a deploy, not a restart.
/// Measured on 2026-11-10: rotating the bot token at BotFather left <c>getWebhookInfo</c> with an
/// empty <c>url</c>, and for three days every message a customer sent the bot was held by Telegram
/// and dropped. The endpoint itself answered 200 the whole time - there was nothing on this side to
/// notice, because Telegram had stopped calling it.
/// </para>
/// <para>
/// The check writes one number: 1 when the feature is on and Telegram has no webhook for it, 0
/// otherwise. A failure to reach Telegram is not written as a number - it propagates, so Hangfire
/// records the run as failed and <c>JobFailed</c> says so. "Missing" on a network error would be a
/// guess, and "healthy" would be worse.
/// </para>
/// </remarks>
public sealed class FeedbackWebhookHealthMonitor
{
    private readonly ITelegramFeedbackClient _bot;
    private readonly FuelFlowMetrics _metrics;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<FeedbackWebhookHealthMonitor> _logger;

    public FeedbackWebhookHealthMonitor(
        ITelegramFeedbackClient bot,
        FuelFlowMetrics metrics,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<FeedbackWebhookHealthMonitor> logger)
    {
        _bot = bot;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task CheckWebhookAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            // Off on purpose: there is no chat a customer is told to write to, and a webhook that
            // still exists is not an incident. The gauge says 0 - measured, not missing.
            _metrics.FeedbackWebhookMissing(false);
            return;
        }

        if (!_options.IsConfigured)
        {
            // Switched on but unprovisioned. The endpoint rejects every delivery (there is no secret
            // to compare) and nothing can be sent back, so this is the same silence as an empty
            // webhook - and there is no token to ask Telegram with.
            _metrics.FeedbackWebhookMissing(true);
            _logger.LogWarning(
                "The feedback bot is enabled but not configured; every message a customer sends it is lost.");
            return;
        }

        var status = await _bot.GetWebhookStatusAsync(ct);
        var missing = string.IsNullOrWhiteSpace(status.Url);
        _metrics.FeedbackWebhookMissing(missing);

        if (missing)
        {
            _logger.LogWarning(
                "The feedback bot has no webhook registered, so {Pending} customer update(s) are "
                + "waiting in Telegram and will expire. Re-register it from the admin Settings screen "
                + "or with setWebhook using the current token and secret (Telegram reports: {Error}).",
                status.PendingUpdateCount,
                status.LastErrorMessage ?? "no error");
        }
    }
}
