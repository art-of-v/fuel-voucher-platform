using System.Text.Json;
using FuelFlow.Features.Feedback;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.Features.Settings;

/// <summary>
/// Reads Telegram's own view of the feedback webhook, for the two places that report it: the
/// settings screen and the answer to a webhook registration.
/// </summary>
/// <remarks>
/// A static helper rather than a service on purpose: it owns no state, it only asks the bot client
/// a question, and a service would then need a registration of its own for two lines of work.
/// </remarks>
internal static class FeedbackWebhookStatusReader
{
    /// <summary>
    /// Current webhook state for the settings screen, or null when Telegram cannot be reached or the
    /// feature is unprovisioned. Null rather than a fabricated "not registered": an unreadable
    /// status and a confirmed-unregistered webhook are different situations and an operator must be
    /// able to tell them apart.
    /// </summary>
    public static async Task<FeedbackWebhookStatusDto?> ReadAsync(
        ITelegramFeedbackClient feedbackBot,
        TelegramFeedbackOptions feedbackOptions,
        CancellationToken cancellationToken)
    {
        if (!feedbackOptions.IsConfigured)
        {
            return null;
        }

        try
        {
            var status = await feedbackBot.GetWebhookStatusAsync(cancellationToken);

            return new FeedbackWebhookStatusDto
            {
                Url = status.Url,
                Registered = !string.IsNullOrWhiteSpace(status.Url),
                PendingUpdateCount = status.PendingUpdateCount,
                LastErrorMessage = status.LastErrorMessage,
                LastErrorDate = status.LastErrorDate
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new FeedbackWebhookStatusDto { LastErrorMessage = ex.Message };
        }
    }
}
