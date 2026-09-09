using FuelFlow.API.Extensions;
using FuelFlow.Features.Support.CreateSupportMessage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FuelFlow.Features.Support;

/// <summary>
/// Public contact endpoint behind the /support page on palne.shop. Anonymous by
/// design, which makes it a spam vector — it is therefore triple-guarded:
/// a per-IP named rate limit, the global per-IP limiter, and a honeypot field
/// ("website") that only bots fill in. Honeypot hits are answered 204 like
/// real submissions so naive bots do not learn from the difference.
/// </summary>
[ApiController]
[Route("api/support")]
[AllowAnonymous]
public sealed class SupportController : ControllerBase
{
    private readonly CreateSupportMessageCommandHandler _handler;

    public SupportController(CreateSupportMessageCommandHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("messages")]
    [EnableRateLimiting(RateLimiterSetup.SupportMessagePolicy)]
    [ProducesResponseType(typeof(CreateSupportMessageResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateMessage(
        CreateSupportMessageCommand command,
        CancellationToken cancellationToken)
    {
        // Honeypot: real users never see this field, so any value means a bot.
        if (!string.IsNullOrWhiteSpace(command.Website))
            return NoContent();

        var userAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

        var result = await _handler.HandleAsync(
            command,
            userAgent,
            // Same source the rate limiter uses: RemoteIpAddress after
            // UseForwardedHeaders has resolved the real client behind Caddy.
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return Accepted(result);
    }
}
