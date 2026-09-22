using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Auth.ConfirmEmailChange;

[ApiController]
[Route("api/auth/email")]
public sealed class EmailChangeController : ControllerBase
{
    private readonly ConfirmEmailChangeCommandHandler _handler;

    public EmailChangeController(ConfirmEmailChangeCommandHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Public confirmation landing for a verified email change. The recipient of the emailed link
    /// opens this; possession of the one-time token is the proof, so no authentication is required.
    /// Returns a minimal self-contained HTML page (option i: no separate frontend route).
    /// </summary>
    [HttpGet("confirm")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm([FromQuery] string? token, CancellationToken cancellationToken)
    {
        var result = await _handler.HandleAsync(new ConfirmEmailChangeCommand(token ?? string.Empty), cancellationToken);

        var (statusCode, heading, message) = result.Status switch
        {
            ConfirmEmailChangeStatus.Confirmed => (StatusCodes.Status200OK,
                "Email confirmed", "Your email address has been updated. You can close this page."),
            ConfirmEmailChangeStatus.Expired => (StatusCodes.Status400BadRequest,
                "Link expired", "This confirmation link has expired. Please request the change again."),
            _ => (StatusCodes.Status400BadRequest,
                "Invalid link", "This confirmation link is invalid or has already been used."),
        };

        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "text/html; charset=utf-8",
            Content = BuildPage(heading, message),
        };
    }

    private static string BuildPage(string heading, string message) =>
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
        "<title>FuelFlow</title>" +
        "<style>body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;" +
        "font-family:-apple-system,Segoe UI,Roboto,sans-serif;background:#0b0b0c;color:#f4f4f5}" +
        ".card{max-width:360px;padding:32px;text-align:center}" +
        "h1{font-size:20px;margin:0 0 8px}p{color:#a1a1aa;font-size:14px;line-height:1.5;margin:0}" +
        ".brand{color:#22d3ee;font-weight:700;letter-spacing:.1em;font-size:12px;margin-bottom:16px}</style>" +
        "</head><body><div class=\"card\"><div class=\"brand\">FUELFLOW</div>" +
        "<h1>" + System.Net.WebUtility.HtmlEncode(heading) + "</h1>" +
        "<p>" + System.Net.WebUtility.HtmlEncode(message) + "</p></div></body></html>";
}
