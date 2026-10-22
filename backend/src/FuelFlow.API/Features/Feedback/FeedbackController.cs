using System.Security.Claims;
using FuelFlow.Features.Settings;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Hands the mobile app a ready-made Telegram deep link for the signed-in user.
/// <para>
/// The app asks for this instead of building the URL itself, and that is the whole point: a
/// signing key inside a shipped binary is a public signing key, and anyone who extracted it could
/// start a chat as any account. The payload is signed here and nowhere else.
/// </para>
/// </summary>
[ApiController]
[Route("api/feedback")]
[Authorize]
public sealed class FeedbackController : ControllerBase
{
    private readonly RuntimeSettingsService _settings;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<FeedbackController> _logger;

    public FeedbackController(
        RuntimeSettingsService settings,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<FeedbackController> logger)
    {
        _settings = settings;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the deep link, or 404 when the feature is off.
    /// <para>
    /// 404 rather than a disabled flag: the mobile app hides the whole tab on a 404, so switching
    /// <c>FeedbackBot:Enabled</c> off in the admin takes the button out of the app without a
    /// release - the same fail-safe shape as <c>VoucherRenewal:Enabled</c>.
    /// </para>
    /// </summary>
    [HttpGet("deep-link")]
    [ProducesResponseType(typeof(FeedbackDeepLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeepLink(CancellationToken ct = default)
    {
        // Both switches, because either one off means the user must not be sent to a bot that will
        // not answer: the runtime toggle is the admin's kill switch, the options flag is whether
        // the integration is provisioned at all.
        if (!await _settings.IsFeedbackBotEnabledAsync(ct) || !_options.IsConfigured)
        {
            _logger.LogInformation("Feedback deep link requested while the feature is disabled.");
            return ApiError.NotFound("Not found");
        }

        var userId = CurrentUserId();
        if (userId is null)
            return ApiError.Unauthorized("Unauthorized");

        var payload = FeedbackDeepLink.CreatePayload(
            userId.Value,
            _options.SigningKey,
            TimeSpan.FromDays(_options.DeepLinkTtlDays),
            SystemClock.UtcNow);

        var url = FeedbackDeepLink.BuildDeepLink(_options.BotUsername, payload);

        // BotUsername is the one value a deployment can forget while everything else looks right;
        // without it there is no link to open, and the app must not open a broken one.
        if (url is null)
        {
            _logger.LogError("Telegram:Feedback is enabled but Telegram:Feedback:BotUsername is empty.");
            return ApiError.NotFound("Not found");
        }

        return Ok(new FeedbackDeepLinkResponse(url));
    }

    private Guid? CurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(claim, out var parsed) ? parsed : null;
    }
}

public sealed record FeedbackDeepLinkResponse(string Url);