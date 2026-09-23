using FuelFlow.SharedKernel.Notifications.Email;
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

    /// <summary>
    /// Self-contained, no-dependency branded landing page: the neon-green cyber-lion on the dark
    /// FuelFlow canvas, matching the transactional emails. The lion is inlined as a data URI (reusing
    /// the same embedded brand asset) so the page needs no external requests.
    /// </summary>
    private static string BuildPage(string heading, string message)
    {
        var logo = EmailBrand.LionLogo();
        var logoTag = logo is null
            ? string.Empty
            : "<img class=\"logo\" alt=\"FuelFlow\" src=\"data:" + logo.MediaType + ";base64," +
              Convert.ToBase64String(logo.Content) + "\">";

        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" +
            "<meta name=\"color-scheme\" content=\"dark light\"><title>FuelFlow</title>" +
            "<style>*{box-sizing:border-box}" +
            "body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;" +
            "font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;" +
            "background:" + EmailBrand.Canvas + ";color:" + EmailBrand.TextPrimary + ";padding:24px}" +
            ".card{width:100%;max-width:400px;padding:40px 32px;text-align:center;background:" + EmailBrand.Surface +
            ";border:1px solid " + EmailBrand.Border + ";border-radius:20px}" +
            ".logo{width:92px;height:92px;display:block;margin:0 auto 18px}" +
            ".brand{color:" + EmailBrand.AccentBright + ";font-weight:700;letter-spacing:.42em;font-size:12px;margin-bottom:22px}" +
            "h1{font-size:22px;margin:0 0 10px;color:" + EmailBrand.TextPrimary + "}" +
            "p{color:" + EmailBrand.TextSecondary + ";font-size:15px;line-height:1.6;margin:0}" +
            ".accent{height:3px;width:44px;background:" + EmailBrand.Accent + ";border-radius:2px;margin:22px auto 0}</style>" +
            "</head><body><div class=\"card\">" + logoTag +
            "<div class=\"brand\">FUELFLOW</div>" +
            "<h1>" + System.Net.WebUtility.HtmlEncode(heading) + "</h1>" +
            "<p>" + System.Net.WebUtility.HtmlEncode(message) + "</p>" +
            "<div class=\"accent\"></div></div></body></html>";
    }
}
