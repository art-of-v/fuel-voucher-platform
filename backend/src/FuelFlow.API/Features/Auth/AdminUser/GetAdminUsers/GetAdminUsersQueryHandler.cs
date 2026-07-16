using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.AdminUser.GetAdminUsers;

public sealed class GetAdminUsersQueryHandler
{
    private readonly ApplicationDbContext _context;
    public GetAdminUsersQueryHandler(ApplicationDbContext context) => _context = context;

    public async Task<List<AdminUserDto>> HandleAsync(GetAdminUsersQuery query, CancellationToken ct = default)
    {
        var items = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(ct);

        return items.Select(u => new AdminUserDto
        {
            Id = u.Id.ToString(),
            Phone = u.PhoneNumber,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Birthdate = u.Birthdate?.ToString("yyyy-MM-dd"),
            ProfileImageUrl = u.ProfileImageUrl,
            ReferralCode = u.ReferralCode,
            ReferredBy = u.ReferredBy,
            BonusBalance = u.BonusBalance,
            Role = u.Role?.Name,
            CreatedAt = u.CreatedAtUtc.ToString("o")
        }).ToList();
    }
}

public sealed class AdminUserDto
{
    public string Id { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Birthdate { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    public decimal BonusBalance { get; set; }
    public string? Role { get; set; }
    public string CreatedAt { get; set; } = null!;
}
