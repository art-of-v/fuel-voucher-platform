using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.GetCurrentUser;

/// <summary>
/// Reads the account behind the caller's token.
/// </summary>
/// <remarks>
/// A deleted account answers like an unknown one: the caller is told nothing, not that their
/// account was removed.
/// </remarks>
public sealed class GetCurrentUserQueryHandler
{
    private readonly ApplicationDbContext _context;

    public GetCurrentUserQueryHandler(ApplicationDbContext context) => _context = context;

    /// <summary>The account, or null when there is none to show.</summary>
    public async Task<CurrentUserDto?> HandleAsync(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == query.UserId, cancellationToken);

        if (user is null || user.IsDeleted) return null;

        return new CurrentUserDto(
            user.Id,
            user.PhoneNumber,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Birthdate,
            user.ProfileImageUrl,
            user.ReferralCode,
            user.ReferredBy,
            user.BonusBalance,
            user.Role?.Name,
            user.CreatedAtUtc);
    }
}
