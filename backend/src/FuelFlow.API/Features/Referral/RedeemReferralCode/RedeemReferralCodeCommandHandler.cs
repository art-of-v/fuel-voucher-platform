using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Referral.RedeemReferralCode;

public sealed class RedeemReferralCodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public RedeemReferralCodeCommandHandler(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RedeemReferralCodeResponse> HandleAsync(RedeemReferralCodeCommand command, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(command.UserId, out var userId))
            throw new ArgumentException("Invalid user ID");

        var code = command.Code.Trim().ToUpperInvariant();

        var redeemer = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (redeemer == null)
            throw new InvalidOperationException("User not found");

        if (!string.IsNullOrEmpty(redeemer.ReferredBy))
            throw new InvalidOperationException("Referral code already redeemed");

        if (redeemer.ReferralCode == code)
            throw new InvalidOperationException("Cannot redeem your own referral code");

        var referrer = await _context.Users
            .FirstOrDefaultAsync(u => u.ReferralCode == code, cancellationToken);

        if (referrer == null)
            throw new InvalidOperationException("Referral code not found");

        redeemer.ReferredBy = code;
        redeemer.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new RedeemReferralCodeResponse(redeemer.Id, code, referrer.Id);
    }
}
