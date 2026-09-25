using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Users.UpdateUser;

public sealed class UpdateUserCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateUserCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UpdateUserResponse> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(command.UserId, out var userId))
            throw new ArgumentException("Invalid user ID");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
            throw new InvalidOperationException("User not found");

        // Email is deliberately NOT handled here. A self-service email change is security-sensitive
        // (for staff the email is a login-OTP delivery channel, so changing it to an attacker address
        // hijacks future admin sign-in codes), so it now requires a fresh OTP step-up and runs through
        // POST /api/users/email/change (RequestEmailChangeCommandHandler). This endpoint still accepts
        // the legacy `email` field from older app builds but ignores it; EmailChangePending is
        // therefore always false here.

        if (command.FirstName is not null)
            user.FirstName = command.FirstName;

        if (command.LastName is not null)
            user.LastName = command.LastName;

        if (command.Birthdate is not null)
            user.Birthdate = command.Birthdate;

        if (command.ProfileImageUrl is not null)
            user.ProfileImageUrl = command.ProfileImageUrl;

        user.UpdatedAtUtc = DateTime.UtcNow;
        _context.Users.Update(user);

        await _context.SaveChangesAsync(cancellationToken);

        return new UpdateUserResponse(
            user.Id,
            user.PhoneNumber,
            user.Email,
            user.FirstName,
            user.LastName,
            user.Birthdate,
            user.ProfileImageUrl,
            user.ReferralCode,
            user.BonusBalance,
            EmailChangePending: false
        );
    }
}
