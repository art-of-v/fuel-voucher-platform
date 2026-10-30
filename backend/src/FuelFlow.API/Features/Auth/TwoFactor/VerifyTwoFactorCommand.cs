using FuelFlow.Features.Auth.Verify;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Auth.TwoFactor;

public sealed record VerifyTwoFactorCommand(string Challenge, string Code);

/// <summary>
/// The second step of a staff sign-in: the code from the authenticator app turns the outstanding
/// challenge into a session.
/// </summary>
/// <remarks>
/// The first step - the OTP - is verified by the ordinary verify handler, which does not mint a
/// session for a staff account: it hands back a challenge instead. This handler is where that
/// challenge is redeemed, and where the session is finally minted - the same session the first
/// factor would have produced, down to the device registration nonce and the staff-login audit
/// row.
/// </remarks>
public sealed class VerifyTwoFactorCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly StaffTwoFactorService _twoFactor;
    private readonly VerifyCodeCommandHandler _sessions;
    private readonly ILogger<VerifyTwoFactorCommandHandler> _logger;

    public VerifyTwoFactorCommandHandler(
        ApplicationDbContext context,
        StaffTwoFactorService twoFactor,
        VerifyCodeCommandHandler sessions,
        ILogger<VerifyTwoFactorCommandHandler> logger)
    {
        _context = context;
        _twoFactor = twoFactor;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<VerifyCodeResponse> HandleAsync(
        VerifyTwoFactorCommand command,
        CancellationToken cancellationToken,
        bool deviceBindingPending = false)
    {
        var result = await _twoFactor.CompleteAsync(command.Challenge, command.Code, cancellationToken);

        if (!result.Succeeded || result.UserId is null)
        {
            // One message for every failure - an unknown challenge, an expired one and a wrong
            // code are the same answer, so the endpoint cannot be used to tell them apart.
            _logger.LogWarning("A staff second-factor attempt was refused");
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(
                u => u.Id == result.UserId && !u.IsDeleted && !u.IsBanned,
                cancellationToken);

        if (user == null)
        {
            // The account went away between the two steps - deleted, banned. The challenge is
            // spent either way: the sign-in does not complete.
            _logger.LogWarning("A staff second factor was proven for an account that cannot sign in");
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        return await _sessions.IssueSessionAsync(user, isNewUser: false, cancellationToken, deviceBindingPending);
    }
}
