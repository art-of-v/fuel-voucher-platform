using FuelFlow.Features.Settings;

namespace FuelFlow.Features.Settings.RegisterFeedbackWebhook;

/// <summary>
/// A webhook registration against this deployment's own public address. The address is built by the
/// controller, because it is read off the incoming request - and the request is the only place the
/// scheme, the host and the path base live.
/// </summary>
public sealed record RegisterFeedbackWebhookCommand(string Url);

/// <summary>
/// The answer of a registration: whether Telegram accepted the webhook, the reason when it did not,
/// and the state Telegram reports afterwards.
/// </summary>
public sealed record RegisterFeedbackWebhookResult(
    bool Success,
    string? Error,
    FeedbackWebhookStatusDto? Webhook);
