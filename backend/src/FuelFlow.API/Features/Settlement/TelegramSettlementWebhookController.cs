using System.Security.Cryptography;
using System.Text;
using FuelFlow.API.Extensions;
using FuelFlow.Features.Feedback;
using FuelFlow.SharedKernel.Database;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Settlement;

/// <summary>
/// Telegram's webhook for the settlement channel.
/// <para>
/// Deliberately a separate endpoint from the feedback bot rather than another branch on it. That bot
/// is public — customers message it — and this one creates money. Routing both through one endpoint
/// would put a public message on the same code path as an instruction to move funds, and a mistake in
/// either would be a mistake about payments.
/// </para>
/// <para>
/// Anonymous by necessity, because Telegram calls it with no bearer token, so the secret header is
/// the only thing proving the delivery is genuine. <b>It proves nothing about who sent the
/// message</b> — the sender allow list in <see cref="SettlementUpdateHandler"/> is the actual boundary,
/// and a delivery that fails it is recorded rather than merely refused.
/// </para>
/// <para>
/// Always answers 2xx for an authenticated delivery, for the reason the feedback webhook does: a
/// non-2xx makes Telegram retry forever, and the unique (chat_id, message_id) index already makes a
/// retry harmless. Retrying would add nothing but load.
/// </para>
/// </summary>
[ApiController]
[Route("api/telegram/settlement/webhook")]
[AllowAnonymous]
[SystemScope]
public sealed class TelegramSettlementWebhookController : ControllerBase
{
    internal const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly SettlementUpdateHandler _handler;
    private readonly TelegramSettlementOptions _options;
    private readonly ILogger<TelegramSettlementWebhookController> _logger;

    public TelegramSettlementWebhookController(
        SettlementUpdateHandler handler,
        IOptions<TelegramSettlementOptions> options,
        ILogger<TelegramSettlementWebhookController> logger)
    {
        _handler = handler;
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
                "Rejected a settlement webhook delivery for update {UpdateId}: bad or missing secret.",
                update?.UpdateId);
            return ApiError.Unauthorized("Unauthorized");
        }

        if (update is null)
            return Ok();

        try
        {
            await _handler.HandleAsync(update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Swallowed for the same reason the feedback webhook swallows: a 500 here would have
            // Telegram redeliver, and the message row is the thing that makes a redelivery safe. The
            // unique index catches a duplicate; this catch keeps one bad message from retrying forever.
            _logger.LogError(ex, "Handling settlement update {UpdateId} failed", update.UpdateId);
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