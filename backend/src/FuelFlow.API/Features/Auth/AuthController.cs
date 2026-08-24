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
    private readonly RefreshTokenCommandHandler _refreshTokenHandler;
    private readonly ApplicationDbContext _context;
    private readonly JwtOptions _jwtOptions;
    private readonly IConfiguration _configuration;

    public AuthController(
        SendCodeCommandHandler sendCodeHandler,
        VerifyCodeCommandHandler verifyCodeHandler,
        RefreshTokenCommandHandler refreshTokenHandler,
        ApplicationDbContext context,
        IOptions<JwtOptions> jwtOptions,
        IConfiguration configuration)
    {
        _sendCodeHandler = sendCodeHandler;
        _verifyCodeHandler = verifyCodeHandler;
        _refreshTokenHandler = refreshTokenHandler;
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

    private void SetRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/api/auth/refresh",
            MaxAge = TimeSpan.FromDays(_jwtOptions.RefreshTokenExpirationDays)
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
            refreshToken = Request.Cookies["refresh_token"];

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

        if (user == null || !user.IsActive) return Unauthorized();

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