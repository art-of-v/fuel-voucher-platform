using FuelFlow.Features.Auth.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.Verify;

/// <summary>
/// Standalone "does the caller currently hold a valid OTP for this phone?" check, used by step-up
/// flows (e.g. the self-service email change) that must re-prove possession of the account WITHOUT
/// logging in or minting tokens. It deliberately mirrors the code-consumption rules in
/// <see cref="VerifyCodeCommandHandler"/> — newest active code wins, a wrong guess is counted and
/// the code is burned once it reaches <see cref="MaxFailedAttempts"/>, a correct code is burned on
/// use — but owns its own SaveChanges because it has no surrounding login transaction to join.
///
/// The login path is intentionally NOT routed through here: there the code burn is committed
/// together with the user/refresh-token writes in a single transaction, and re-pointing it at a
/// service that saves on its own would move that boundary. If you change the consumption rules in
/// one place, change them in both.
/// </summary>
public sealed class OtpVerificationService
{
    /// <summary>Wrong guesses allowed per issued code before it is invalidated. Kept in lockstep
    /// with <see cref="VerifyCodeCommandHandler.MaxFailedAttempts"/>.</summary>
    public const int MaxFailedAttempts = 5;

    private readonly ApplicationDbContext _context;
    private readonly AuthOptions _authOptions;

    public OtpVerificationService(ApplicationDbContext context, IOptions<AuthOptions> authOptions)
    {
        _context = context;
        _authOptions = authOptions.Value;
    }

    /// <summary>
    /// Consumes the newest active verification code for <paramref name="normalizedPhone"/>:
    /// returns true and burns the code on an exact match; on a mismatch counts a failed attempt
    /// (burning the code once it reaches <see cref="MaxFailedAttempts"/>) and returns false; returns
    /// false when no live code exists. <paramref name="normalizedPhone"/> MUST already be normalized
    /// by the caller (pass the stored <c>User.PhoneNumber</c>, which is the canonical form).
    /// </summary>
    public async Task<bool> TryConsumeAsync(string normalizedPhone, string code, CancellationToken cancellationToken)
    {
        var trimmed = (code ?? string.Empty).Trim();

        // Load the newest active code regardless of whether it matches, so a wrong guess can be
        // counted against it (matches the login path's brute-force accounting).
        var verificationCode = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == normalizedPhone && !v.IsUsed && v.ExpiresAtUtc > SystemClock.UtcNow)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode == null)
            return false;

        // A digest written before the OTP pepper existed can never match a submitted code, so
        // burn it instead of counting a guess against it. See VerifyCodeCommandHandler for the
        // same rule on the login path.
        if (!OtpCodeHasher.IsPeppered(verificationCode.Code))
        {
            verificationCode.IsUsed = true;
            verificationCode.UsedAtUtc = SystemClock.UtcNow;
            _context.VerificationCodes.Update(verificationCode);
            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }

        if (!OtpCodeHasher.Verify(verificationCode.Code, trimmed, _authOptions.OtpPepper))
        {
            verificationCode.FailedAttempts++;
            if (verificationCode.FailedAttempts >= MaxFailedAttempts)
            {
                verificationCode.IsUsed = true;
                verificationCode.UsedAtUtc = SystemClock.UtcNow;
            }

            _context.VerificationCodes.Update(verificationCode);
            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }

        verificationCode.IsUsed = true;
        verificationCode.UsedAtUtc = SystemClock.UtcNow;
        _context.VerificationCodes.Update(verificationCode);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
