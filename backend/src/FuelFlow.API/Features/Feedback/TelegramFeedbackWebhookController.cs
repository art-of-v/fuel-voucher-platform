using System.Security.Cryptography;
using System.Text;
using FuelFlow.API.Extensions;
using FuelFlow.Features.Feedback;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Feedback;

/// <summary>
/// Telegram's webhook for the feedback bot.
/// <para>
/// Anonymous by necessity - Telegram calls it with no bearer token - so the only credential is
/// <c>X-Telegram-Bot-Api-Secret-Token</c>, compared in constant time. That header is not decoration:
/// without it, anyone who learns the URL could post fake updates into a customer's chat, bind
/// themselves to an account, or flood the staff group. This endpoint is in the reviewed anonymous
/// set pinned by <c>AnonymousEndpointSurfaceTests</c>.
/// </para>
/// <para>
/// Always answers 2xx for an authenticated delivery. A non-2xx makes Telegram retry the update
/// forever, and the only way that happens here is a bug - the message is already in the database
/// by the time anything can fail, so there is nothing a retry would recover.
/// </para>
/// </summary>
[ApiController]
[Route("api/telegram/feedback/webhook")]
[AllowAnonymous]
public sealed class TelegramFeedbackWebhookController : ControllerBase
{
    internal const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly FeedbackUpdateRouter _router;
    private readonly TelegramFeedbackOptions _options;
    private readonly ILogger<TelegramFeedbackWebhookController> _logger;

    public TelegramFeedbackWebhookController(
        FeedbackUpdateRouter router,
        IOptions<TelegramFeedbackOptions> options,
        ILogger<TelegramFeedbackWebhookController> logger)
    {
        _router = router;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimiterSetup.TelegramFeedbackWebhookPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Receive([FromBody] TelegramUpdate? update, CancellationToken ct)
    {
        if (!SecretMatches(Request.Headers[SecretHeader]))
        {
            _logger.LogWarning(
                "Rejected a Telegram feedback webhook delivery for update {UpdateId}: bad or missing secret.",
                update?.UpdateId);
            return ApiError.Unauthorized("Unauthorized");
        }

        if (update is null)
            return Ok();

        try
        {
            await _router.HandleAsync(update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed deliberately. Returning 500 here would have Telegram redeliver this update
            // - which is how one customer's message ends up in the staff group three times. The row
            // is written before anything that can fail, so the message is already safe.
            _logger.LogError(ex, "Handling Telegram feedback update {UpdateId} failed", update.UpdateId);
        }

        return Ok();
    }

    private bool SecretMatches(string? presented)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret) || string.IsNullOrWhiteSpace(presented))
            return false;

        var expected = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        var actual = Encoding.UTF8.GetBytes(presented);

        return expected.Length == actual.Length
               && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}