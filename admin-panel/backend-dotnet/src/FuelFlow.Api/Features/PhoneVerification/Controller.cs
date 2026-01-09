using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Features.Users;
using FuelFlow.Api.Infrastructure.Services;

namespace FuelFlow.Api.Features.PhoneVerification;

[ApiController]
[Route("api/auth/phone")]
public class PhoneVerificationController : ControllerBase
{
    private readonly IPhoneVerificationRepository _verificationRepo;
    private readonly IUserRepository _userRepo;
    private readonly ITwilioService _twilioService;
    private static readonly Dictionary<string, (int count, DateTime resetAt)> _rateLimitTracker = new();

    public PhoneVerificationController(
        IPhoneVerificationRepository verificationRepo,
        IUserRepository userRepo,
        ITwilioService twilioService)
    {
        _verificationRepo = verificationRepo;
        _userRepo = userRepo;
        _twilioService = twilioService;
    }

    [HttpPost("send-code")]
    public async Task<IActionResult> SendCode([FromBody] SendCodeRequest request)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"send:{clientIp}";

        if (IsRateLimited(key, 3, TimeSpan.FromMinutes(1)))
            return StatusCode(429, new { error = "Too many requests. Please wait a minute." });

        if (string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { error = "Phone number is required" });

        var sanitizedPhone = request.Phone.Replace(" ", "").Replace("-", "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(sanitizedPhone, @"^\+?[1-9]\d{6,14}$"))
            return BadRequest(new { error = "Invalid phone number format" });

        var normalizedPhone = sanitizedPhone.StartsWith("+") ? sanitizedPhone : $"+{sanitizedPhone}";
        var code = GenerateVerificationCode();
        var isSent = await _twilioService.SendVerificationCodeAsync(normalizedPhone, code);

        if (!isSent)
            return StatusCode(500, new { error = "Failed to send SMS" });

        await _verificationRepo.CreateAsync(normalizedPhone, code);

        return Ok(new { success = true, message = "Verification code sent" });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyCodeRequest request)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"verify:{clientIp}";

        if (IsRateLimited(key, 5, TimeSpan.FromMinutes(5)))
            return StatusCode(429, new { error = "Too many attempts. Please wait 5 minutes." });

        if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Phone and code are required" });

        if (request.Code.Length != 6 || !request.Code.All(char.IsDigit))
            return BadRequest(new { error = "Invalid code format" });

        var normalizedPhone = request.Phone.StartsWith("+") ? request.Phone : $"+{request.Phone}";
        var verification = await _verificationRepo.GetLatestAsync(normalizedPhone);

        if (verification == null)
            return BadRequest(new { error = "No verification pending or code expired" });

        if (verification.Code != request.Code)
            return BadRequest(new { error = "Invalid verification code" });

        await _verificationRepo.MarkVerifiedAsync(verification.Id);

        var user = await _userRepo.GetByPhoneAsync(normalizedPhone);
        if (user == null)
            user = await _userRepo.CreateWithPhoneAsync(normalizedPhone);

        HttpContext.Session.SetString("userId", user.Id);
        HttpContext.Session.SetString("phoneAuth", "true");

        return Ok(new { success = true, user });
    }

    [HttpGet("user")]
    public async Task<IActionResult> GetUser()
    {
        var userId = HttpContext.Session.GetString("userId");
        var isPhoneAuth = HttpContext.Session.GetString("phoneAuth");

        if (string.IsNullOrEmpty(userId) || isPhoneAuth != "true")
            return Unauthorized(new { error = "Not authenticated" });

        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null)
            return NotFound(new { error = "User not found" });

        return Ok(user);
    }

    private static bool IsRateLimited(string key, int limit, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        
        if (_rateLimitTracker.TryGetValue(key, out var entry))
        {
            if (entry.resetAt < now)
            {
                _rateLimitTracker[key] = (1, now.Add(window));
                return false;
            }

            if (entry.count >= limit)
                return true;

            _rateLimitTracker[key] = (entry.count + 1, entry.resetAt);
            return false;
        }

        _rateLimitTracker[key] = (1, now.Add(window));
        return false;
    }

    private static string GenerateVerificationCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }
}
