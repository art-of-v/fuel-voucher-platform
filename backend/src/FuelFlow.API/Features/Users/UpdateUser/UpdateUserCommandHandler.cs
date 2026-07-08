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

        if (command.Email is not null)
            user.Email = command.Email;

        if (command.FirstName is not null)
            user.FirstName = command.FirstName;

        if (command.LastName is not null)
            user.LastName = command.LastName;

        if (command.Birthdate is not null)
            user.Birthdate = command.Birthdate;

        if (command.ProfileImageUrl is not null)
            user.ProfileImageUrl = command.ProfileImageUrl;

        user.UpdatedAtUtc = DateTime.UtcNow;

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
            user.BonusBalance
        );
    }
}
