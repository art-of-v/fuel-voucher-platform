using FuelFlow.Features.Auth.AdminLogin;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Services;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Auth;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly SendCodeCommandHandler _sendCodeHandler;
    private readonly VerifyCodeCommandHandler _verifyCodeHandler;
    private readonly AdminSendCodeCommandHandler _adminSendCodeHandler;
    private readonly AdminVerifyCodeCommandHandler _adminVerifyCodeHandler;
    private readonly RefreshTokenCommandHandler _refreshTokenHandler;
    private readonly LogoutSessionCommandHandler _logoutSessionHandler;
    private readonly ApplicationDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private readonly IConfiguration _configuration;

    public AuthController(
        SendCodeCommandHandler sendCodeHandler,
        VerifyCodeCommandHandler verifyCodeHandler,
        AdminSendCodeCommandHandler adminSendCodeHandler,
        AdminVerifyCodeCommandHandler adminVerifyCodeHandler,
        RefreshTokenCommandHandler refreshTokenHandler,
        LogoutSessionCommandHandler logoutSessionHandler,
        ApplicationDbContext context,
        IOptions<JwtOptions> jwtOptions,
        IConfiguration configuration)
    {
        _sendCodeHandler = sendCodeHandler;
        _verifyCodeHandler = verifyCodeHandler;
        _adminSendCodeHandler = adminSendCodeHandler;
        _adminVerifyCodeHandler = adminVerifyCodeHandler;
        _refreshTokenHandler = refreshTokenHandler;
        _logoutSessionHandler = logoutSessionHandler;
        _context = context;
        _jwtOptions = jwtOptions.Value;
        _configuration = configuration;
    }

    private bool IsAllowedOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
            return true;

        var allowed = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private const string RefreshTokenCookieName = "refresh_token";

    // The cookie is scoped here and stays here. The logout endpoint lives at
    // "refresh/logout" (a sub-path of this) specifically so the browser sends this
    // same cookie to it - by RFC 6265 path matching - without us having to widen the
    // path. Widening (e.g. to "/api/auth") would leave already-issued cookies stranded
    // at the old path: the browser would then hold two "refresh_token" cookies and send
    // the stale one first, tripping refresh-token reuse detection into a logout loop.
    private const string RefreshTokenCookiePath = "/api/auth/refresh";

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = RefreshTokenCookiePath,
            MaxAge = TimeSpan.FromDays(_jwtOptions.RefreshTokenExpirationDays)
        });
    }

    private void ClearRefreshTokenCookie()
    {
        // Path + Secure + SameSite must match the Append call above, or the browser
        // keeps the cookie.
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = RefreshTokenCookiePath
        });
    }

    [HttpPost("send-code")]
    [AllowAnonymous]
    [EnableRateLimiting(SendCodePolicy)]
    [ProducesResponseType(typeof(SendCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendCode([FromBody] SendCodeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PhoneNumber))
            return BadRequest(new { message = "Phone number is required" });

        try
        {
            var result = await _sendCodeHandler.HandleAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (SmsBudgetExhaustedException)
        {
            // The daily spend ceiling tripped. Answer 503 rather than 500 so the client backs
            // off instead of retrying, and so this is distinguishable in logs from a Twilio fault.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "SMS delivery is temporarily unavailable. Please try again later." });
        }
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(VerifyCodePolicy)]
    [ProducesResponseType(typeof(VerifyCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Verify([FromBody] VerifyCodeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PhoneNumber))
            return BadRequest(new { message = "Phone number is required" });

        if (string.IsNullOrWhiteSpace(command.Code))
            return BadRequest(new { message = "Verification code is required" });

        try
        {
            var result = await _verifyCodeHandler.HandleAsync(command, cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    // Admin-panel login. Distinct from send-code/verify because it authorizes BEFORE sending a
    // code (spec §13): only an existing staff account receives an OTP, and only a staff account
    // can complete verify. The admin SPA calls these; the shared /send-code and /verify are removed
    // from the admin reverse-proxy allow-list, so the auto-registering mobile path is unreachable
    // from the admin domain. Same rate-limit policies as their mobile counterparts.
    [HttpPost("admin/send-code")]
    [AllowAnonymous]
    [EnableRateLimiting(SendCodePolicy)]
    [ProducesResponseType(typeof(SendCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AdminSendCode([FromBody] AdminSendCodeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PhoneNumber))
            return BadRequest(new { message = "Phone number is required" });

        try
        {
            var result = await _adminSendCodeHandler.HandleAsync(command, cancellationToken);
            return Ok(result);
        }
        catch (SmsBudgetExhaustedException)
        {
            // Do NOT surface 503 here the way the mobile /send-code does. On the admin path the
            // budget only trips for a STAFF phone routed to SMS (a non-staff phone returns 200
            // without ever touching the budget), so a distinct status code would turn budget
            // exhaustion into a staff-vs-non-staff oracle. Return the same 200 the non-staff
            // branch returns; the failure is logged server-side by the SMS budget guard.
            return Ok(new SendCodeResponse(true));
        }
    }

    [HttpPost("admin/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(VerifyCodePolicy)]
    [ProducesResponseType(typeof(VerifyCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AdminVerify([FromBody] AdminVerifyCodeCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.PhoneNumber))
            return BadRequest(new { message = "Phone number is required" });

        if (string.IsNullOrWhiteSpace(command.Code))
            return BadRequest(new { message = "Verification code is required" });

        try
        {
            var result = await _adminVerifyCodeHandler.HandleAsync(command, cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RefreshPolicy)]
    [ProducesResponseType(typeof(RefreshTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand? command, CancellationToken cancellationToken)
    {
        // CSRF defense: the refresh cookie is SameSite=None (admin SPA is a
        // different origin than the API), so any cross-site page can trigger a
        // cookie-authenticated POST to this route. Browsers always send Origin
        // on such requests — reject any Origin that isn't explicitly allow-listed.
        // Requests without an Origin (native app, curl) are unaffected.
        if (!IsAllowedOrigin(Request))
            return Unauthorized(new { message = "Origin not allowed" });

        var refreshToken = command?.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
            refreshToken = Request.Cookies[RefreshTokenCookieName];

        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        try
        {
            var result = await _refreshTokenHandler.HandleAsync(new RefreshTokenCommand(refreshToken), cancellationToken);
            SetRefreshTokenCookie(result.RefreshToken);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    // Path is "refresh/logout", not "logout", so the httpOnly refresh cookie (scoped to
    // /api/auth/refresh) is sent here and can be revoked + cleared. See RefreshTokenCookiePath.
    [HttpPost("refresh/logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenCommand? command, CancellationToken cancellationToken)
    {
        // Same CSRF defense as Refresh: the refresh cookie is SameSite=None, so a
        // cross-site page could POST here with the victim's cookie. That would only
        // log them out (annoying, not dangerous), but the origin allow-list is cheap
        // and keeps this route consistent with /refresh.
        if (!IsAllowedOrigin(Request))
            return Unauthorized(new { message = "Origin not allowed" });

        var refreshToken = command?.RefreshToken;

        if (string.IsNullOrWhiteSpace(refreshToken))
            refreshToken = Request.Cookies[RefreshTokenCookieName];

        // Revoke the presented token server-side so a captured cookie can't be
        // replayed after logout. No-op when absent/unknown - logout is idempotent.
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _logoutSessionHandler.HandleAsync(new LogoutSessionCommand(refreshToken), cancellationToken);

        // Always clear the cookie so a page refresh can't silently re-authenticate.
        ClearRefreshTokenCookie();

        return Ok(new { message = "Logged out successfully" });
    }
    [HttpGet("user/me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null || user.IsDeleted) return Unauthorized();

        return Ok(new
        {
            id = user.Id,
            phone = user.PhoneNumber,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            birthdate = user.Birthdate,
            profileImageUrl = user.ProfileImageUrl,
            referralCode = user.ReferralCode,
            referredBy = user.ReferredBy,
            bonusBalance = user.BonusBalance,
            role = user.Role?.Name,
            createdAt = user.CreatedAtUtc
        });
    }
}