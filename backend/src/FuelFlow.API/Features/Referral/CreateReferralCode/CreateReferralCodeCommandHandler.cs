using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Referral.CreateReferralCode;

public sealed class CreateReferralCodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateReferralCodeCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CreateReferralCodeResponse> HandleAsync(CreateReferralCodeCommand command, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(command.UserId, out var userId))
            throw new ArgumentException("Invalid user ID");

        var code = command.Code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Referral code cannot be empty");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
            throw new InvalidOperationException("User not found");

        if (!string.IsNullOrEmpty(user.ReferralCode))
            throw new InvalidOperationException("Referral code already set");

        var taken = await _context.Users
            .AnyAsync(u => u.ReferralCode == code, cancellationToken);

        if (taken)
            throw new InvalidOperationException("Referral code already taken");

        user.ReferralCode = code;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new CreateReferralCodeResponse(user.Id, code);
    }
}
