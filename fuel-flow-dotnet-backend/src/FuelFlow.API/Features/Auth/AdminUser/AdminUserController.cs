using FuelFlow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminUser;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "Admin")]
public sealed class AdminUserController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminUserController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var result = items.Select(u => new
        {
            id = u.Id.ToString(),
            email = (string?)null,
            phone = u.PhoneNumber,
            firstName = (string?)null,
            lastName = (string?)null,
            birthdate = (string?)null,
            profileImageUrl = (string?)null,
            referralCode = u.ReferralCode,
            referredBy = u.ReferredBy,
            bonusBalance = u.BonusBalance,
            createdAt = u.CreatedAtUtc.ToString("o")
        });

        return Ok(result);
    }
}
