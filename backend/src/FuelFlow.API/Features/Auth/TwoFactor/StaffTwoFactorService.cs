using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Vouchers.Security;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace FuelFlow.Features.Auth.TwoFactor;

/// <summary>What the client has to do next to finish a staff sign-in.</summary>
public enum TwoFactorStatus
{
    /// <summary>The account has a secret; the client has to send a code from the authenticator app.</summary>
    Required,

    /// <summary>The account has no secret yet; the client has to enrol one and prove it.</summary>
    Enroll
}

/// <summary>
/// The outstanding challenge: the proof that the first factor was verified, and what the client
/// has to do with it.
/// </summary>
public sealed record StaffTwoFactorChallenge(
    TwoFactorStatus Status,
    string Challenge,
    string? EnrollmentSecret,
    string? ProvisioningUri);

/// <summary>The outcome of a second-factor attempt.</summary>
public sealed record StaffTwoFactorResult(bool Succeeded, Guid? UserId);

/// <summary>
/// The second factor of a staff sign-in: enrol it, prove it, and take it back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape of the flow.</b> The first factor (the OTP) is verified by the ordinary verify
/// handler. That handler does not mint a session for a staff account: it asks this service for a
/// challenge and returns it to the client. The client answers with the challenge and a code from
/// the authenticator app, and only then is the session minted. A staff sign-in therefore cannot
/// complete with the OTP alone - which is the whole point of the finding.
/// </para>
/// <para>
/// <b>The challenge.</b> It is a random 32-byte token; only its hash is stored, so a database
/// leak cannot be replayed against the endpoint. It expires in five minutes and is consumed by
/// the first attempt that uses it, whether that attempt succeeds or not, so a challenge cannot be
/// brute-forced by trying codes against it.
/// </para>
/// <para>
/// <b>The enrolment.</b> An account with no secret yet gets one: the server generates it, keeps
/// it in the pending columns and hands it to the client. The client shows it to the user, the
/// user types it into an authenticator app, and the app's first code is what proves the
/// enrolment. Until that code arrives the secret is not a factor - it is a candidate for one.
/// </para>
/// </remarks>
public sealed class StaffTwoFactorService
{
    /// <summary>
    /// Wrong codes allowed per challenge. A six-digit code space is small enough that five
    /// attempts per challenge is the difference between "unlucky" and "brute force".
    /// </summary>
    internal const int MaxFailedAttempts = 5;

    /// <summary>How long a challenge stays redeemable.</summary>
    internal static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _context;
    private readonly IQrCipher _cipher;
    private readonly IOptions<AuthOptions> _authOptions;
    private readonly ILogger<StaffTwoFactorService> _logger;

    public StaffTwoFactorService(
        ApplicationDbContext context,
        IQrCipher cipher,
        IOptions<AuthOptions> authOptions,
        ILogger<StaffTwoFactorService> logger)
    {
        _context = context;
        _cipher = cipher;
        _authOptions = authOptions;
        _logger = logger;
    }

    /// <summary>
    /// Whether this account has a second factor enrolled.
    /// </summary>
    public async Task<bool> IsEnrolledAsync(Guid userId, CancellationToken cancellationToken)
    {
        var enrolled = await _context.StaffTwoFactors
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.SecretCiphertext != null)
            .AnyAsync(cancellationToken);

        return enrolled;
    }

    /// <summary>
    /// Issues the outstanding challenge for a staff account, after the first factor was verified.
    /// </summary>
    public async Task<StaffTwoFactorChallenge> IssueChallengeAsync(User user, CancellationToken cancellationToken)
    {
        var challenge = NewChallengeToken();
        var challengeHash = SecretsHasher.Hash(challenge);
        var now = SystemClock.UtcNow;

        var factor = await _context.StaffTwoFactors
            .FirstOrDefaultAsync(f => f.UserId == user.Id, cancellationToken);

        if (factor == null)
        {
            factor = new StaffTwoFactor
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _context.StaffTwoFactors.Add(factor);
        }

        factor.PendingChallengeHash = challengeHash;
        factor.PendingExpiresAtUtc = now.Add(ChallengeTtl);
        factor.FailedAttempts = 0;
        factor.UpdatedAtUtc = now;

        string? enrollmentSecret = null;
        string? provisioningUri = null;
        var status = TwoFactorStatus.Required;

        if (factor.SecretCiphertext == null)
        {
            // No secret yet: this sign-in is the enrolment. The secret is a candidate until the
            // client proves it, so it lives in the pending columns and the next attempt
            // overwrites it.
            enrollmentSecret = Totp.GenerateSecret();
            factor.PendingSecretCiphertext = _cipher.Encrypt(enrollmentSecret);
            provisioningUri = Totp.BuildProvisioningUri(
                _authOptions.Value.StaffTotpIssuer,
                user.PhoneNumber,
                enrollmentSecret);
            status = TwoFactorStatus.Enroll;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Staff second factor {Status} for user {UserId}; the challenge expires at {ExpiresAtUtc}",
            status,
            user.Id,
            factor.PendingExpiresAtUtc);

        return new StaffTwoFactorChallenge(status, challenge, enrollmentSecret, provisioningUri);
    }

    /// <summary>
    /// Redeems a challenge with a code from the authenticator app.
    /// </summary>
    public async Task<StaffTwoFactorResult> CompleteAsync(
        string challenge,
        string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(challenge) || string.IsNullOrWhiteSpace(code))
            return new StaffTwoFactorResult(false, null);

        var challengeHash = SecretsHasher.Hash(challenge.Trim());
        var now = SystemClock.UtcNow;

        var factor = await _context.StaffTwoFactors
            .FirstOrDefaultAsync(
                f => f.PendingChallengeHash == challengeHash
                     && f.PendingExpiresAtUtc != null
                     && f.PendingExpiresAtUtc > now,
                cancellationToken);

        if (factor == null)
        {
            // No oracle: an unknown, expired or already-used challenge gets the same refusal
            // as a wrong code.
            _logger.LogWarning("A staff second-factor attempt arrived with no outstanding challenge");
            return new StaffTwoFactorResult(false, null);
        }

        var stored = factor.SecretCiphertext ?? factor.PendingSecretCiphertext;
        if (stored == null)
        {
            // The row claims a challenge but carries no secret: the enrolment was superseded.
            await BurnChallengeAsync(factor, now, cancellationToken);
            return new StaffTwoFactorResult(false, null);
        }

        string secret;
        try
        {
            secret = _cipher.Decrypt(stored);
        }
        catch (CryptographicException ex)
        {
            // The key does not match the ciphertext - a rotated key, or a row written by
            // something else. The factor cannot be proven, and it cannot be silently ignored.
            _logger.LogError(ex, "The stored second-factor secret for user {UserId} cannot be decrypted", factor.UserId);
            return new StaffTwoFactorResult(false, null);
        }

        byte[] secretBytes;
        try
        {
            secretBytes = Totp.DecodeBase32(secret);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "The stored second-factor secret for user {UserId} is not a valid secret", factor.UserId);
            return new StaffTwoFactorResult(false, null);
        }

        if (!Totp.VerifyCode(secretBytes, code, now))
        {
            factor.FailedAttempts++;
            factor.UpdatedAtUtc = now;

            if (factor.FailedAttempts >= MaxFailedAttempts)
            {
                _logger.LogWarning(
                    "The second-factor challenge for user {UserId} was burned after {MaxAttempts} failed attempts",
                    factor.UserId,
                    MaxFailedAttempts);
                await BurnChallengeAsync(factor, now, cancellationToken);
            }
            else
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return new StaffTwoFactorResult(false, null);
        }

        // The code is right. An enrolment becomes a factor here, and only here.
        if (factor.SecretCiphertext == null && factor.PendingSecretCiphertext != null)
        {
            factor.SecretCiphertext = factor.PendingSecretCiphertext;
            factor.PendingSecretCiphertext = null;
            _logger.LogInformation("The second factor of user {UserId} was enrolled", factor.UserId);
        }

        factor.PendingChallengeHash = null;
        factor.PendingExpiresAtUtc = null;
        factor.FailedAttempts = 0;
        factor.LastVerifiedAtUtc = now;
        factor.UpdatedAtUtc = now;

        await _context.SaveChangesAsync(cancellationToken);

        return new StaffTwoFactorResult(true, factor.UserId);
    }

    /// <summary>
    /// Takes the second factor away from an account. The next sign-in enrols a new one.
    /// </summary>
    public async Task<bool> RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var factor = await _context.StaffTwoFactors
            .FirstOrDefaultAsync(f => f.UserId == userId, cancellationToken);

        if (factor == null)
            return false;

        factor.SecretCiphertext = null;
        factor.PendingSecretCiphertext = null;
        factor.PendingChallengeHash = null;
        factor.PendingExpiresAtUtc = null;
        factor.FailedAttempts = 0;
        factor.LastVerifiedAtUtc = null;
        factor.UpdatedAtUtc = SystemClock.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("The second factor of user {UserId} was revoked", userId);
        return true;
    }

    private async Task BurnChallengeAsync(
        StaffTwoFactor factor,
        DateTime now,
        CancellationToken cancellationToken)
    {
        factor.PendingChallengeHash = null;
        factor.PendingExpiresAtUtc = null;
        factor.PendingSecretCiphertext = null;
        factor.FailedAttempts = 0;
        factor.UpdatedAtUtc = now;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string NewChallengeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
