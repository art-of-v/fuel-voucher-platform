using FuelFlow.Features.Feedback;
using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Settings.RegisterFeedbackWebhook;

/// <summary>
/// Registers the feedback webhook against this deployment's own public address, so an operator
/// does not have to hand-assemble the URL and paste it into a terminal.
/// </summary>
public sealed class RegisterFeedbackWebhookCommandHandler
{
    private readonly ITelegramFeedbackClient _feedbackBot;
    private readonly TelegramFeedbackOptions _feedbackOptions;

    public RegisterFeedbackWebhookCommandHandler(
        ITelegramFeedbackClient feedbackBot,
        IOptions<TelegramFeedbackOptions> feedbackOptions)
    {
        _feedbackBot = feedbackBot;
        _feedbackOptions = feedbackOptions.Value;
    }

    public async Task<RegisterFeedbackWebhookResult> HandleAsync(
        RegisterFeedbackWebhookCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_feedbackOptions.IsConfigured)
        {
            return new RegisterFeedbackWebhookResult(
                false,
                "Telegram:Feedback is not configured. Set the bot token, staff chat, webhook " +
                "secret and signing key, then restart the API.",
                null);
        }

        try
        {
            await _feedbackBot.SetWebhookAsync(command.Url, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            // Telegram's own error text is the useful part ("Bad webhook: ..."), so it reaches the
            // admin screen verbatim instead of being flattened into a 500.
            return new RegisterFeedbackWebhookResult(false, ex.Message, null);
        }

        return new RegisterFeedbackWebhookResult(
            true,
            null,
            await FeedbackWebhookStatusReader.ReadAsync(_feedbackBot, _feedbackOptions, cancellationToken));
    }
}
