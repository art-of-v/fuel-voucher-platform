using Microsoft.AspNetCore.Mvc;
using FuelFlow.Api.Features.Users;
using FuelFlow.Api.Features.Notifications;

namespace FuelFlow.Api.Features.Referral;

[ApiController]
[Route("api/referral")]
public class ReferralController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly INotificationRepository _notificationRepo;

    public ReferralController(IUserRepository userRepo, INotificationRepository notificationRepo)
    {
        _userRepo = userRepo;
        _notificationRepo = notificationRepo;
    }

    [HttpPost("create")]
    public async Task<IActionResult> CreateReferralCode([FromBody] CreateReferralRequest request)
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";

        var existing = await _userRepo.GetByReferralCodeAsync(request.Code);
        if (existing != null)
            return BadRequest(new { error = "Referral code already taken" });

        var user = await _userRepo.UpdateReferralCodeAsync(userId, request.Code);
        return Ok(user);
    }

    [HttpPost("redeem")]
    public async Task<IActionResult> RedeemReferralCode([FromBody] RedeemReferralRequest request)
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";

        var referrer = await _userRepo.GetByReferralCodeAsync(request.Code);
        if (referrer == null)
            return NotFound(new { error = "Invalid referral code" });

        if (referrer.Id == userId)
            return BadRequest(new { error = "Cannot redeem your own code" });

        var currentUser = await _userRepo.GetByIdAsync(userId);
        if (!string.IsNullOrEmpty(currentUser?.ReferredBy))
            return BadRequest(new { error = "You have already redeemed a referral code" });

        await _userRepo.UpdateReferredByAsync(userId, referrer.Id);

        var referrerBonus = (referrer.BonusBalance) + 50;
        await _userRepo.UpdateBonusBalanceAsync(referrer.Id, referrerBonus);

        var userBonus = (currentUser?.BonusBalance ?? 0) + 20;
        await _userRepo.UpdateBonusBalanceAsync(userId, userBonus);

        await _notificationRepo.CreateAsync(new CreateNotificationRequest
        {
            UserId = referrer.Id,
            Title = "New Referral Reward!",
            Message = $"User {currentUser?.Phone ?? "someone"} used your code! You got 50 UAH bonus."
        });

        await _notificationRepo.CreateAsync(new CreateNotificationRequest
        {
            UserId = userId,
            Title = "Welcome Bonus!",
            Message = $"You redeemed code {request.Code} and received 20 UAH bonus!"
        });

        return Ok(new { success = true, message = "Referral code redeemed" });
    }
}

public record CreateReferralRequest
{
    public string Code { get; init; } = string.Empty;
}

public record RedeemReferralRequest
{
    public string Code { get; init; } = string.Empty;
}
