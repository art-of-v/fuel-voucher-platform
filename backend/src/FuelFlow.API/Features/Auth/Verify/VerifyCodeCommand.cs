using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Security;
using FuelFlow.Features.Auth.TwoFactor;
using FuelFlow.Features.Brands;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text.Json;

namespace FuelFlow.Features.Auth.Verify;

public sealed record VerifyCodeCommand(string PhoneNumber, string Code);

/// <summary>
/// What a successful verification hands back.
/// </summary>
/// <remarks>
/// <para>
/// The refresh token is nullable because it is not for every caller: it travels in the body only
/// for the native mobile app, which has no cookie jar to keep it in. A browser gets the
/// <c>HttpOnly</c> cookie and no token in the body — see <c>ClientPlatform</c>.
/// </para>
/// <para>
/// The access token is nullable for the same kind of reason: a staff sign-in answers with a
/// <see cref="TwoFactorChallengeResponse"/> instead of a session, because the first factor is not
/// enough. A response without a token and without a challenge is not a thing this API produces.
/// </para>
/// </remarks>
public sealed record VerifyCodeResponse(
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    string DeviceRegistrationNonce,
    TwoFactorChallengeResponse? TwoFactor = null
);

public sealed class VerifyCodeCommandHandler
{
    /// <summary>
    /// Wrong guesses allowed per issued code before it is invalidated. With a
    /// 6-digit code space this caps a brute-force attack at 5 guesses per
    /// code; combined with the send-code rate limit the expected guesses
    /// needed to hit one code exceed 100k requests.
    /// </summary>
    internal const int MaxFailedAttempts = 5;

    /// <summary>
    /// Redis key prefix and lifetime for the one-time device registration nonce
    /// returned with every successful verify. The register-device endpoint burns
    /// this nonce to authorize rebinding a device_id that another user previously
    /// enrolled - the only legitimate way a phone changes hands. Keyed by user id,
    /// so a nonce is worthless for any other account.
    /// </summary>
    internal const string RegistrationNonceKeyPrefix = "devreg:";
    internal static readonly TimeSpan RegistrationNonceTtl = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _tokenService;
    private readonly IPhoneNumberService _phoneNumberService;
    private readonly JwtOptions _jwtOptions;
    private readonly AuthOptions _authOptions;
    private readonly StaffTwoFactorService _twoFactor;
    private readonly ILogger<VerifyCodeCommandHandler> _logger;
    private readonly AuditEventService _eventService;
    private readonly NotificationDispatcher? _notifications;
    private readonly IConnectionMultiplexer? _redis;
    private readonly QaTestAccess.QaTestAccessService? _qaTestAccess;
    private readonly FuelFlowMetrics? _metrics;

    public VerifyCodeCommandHandler(
        ApplicationDbContext context,
        IJwtTokenService tokenService,
        IPhoneNumberService phoneNumberService,
        IOptions<JwtOptions> jwtOptions,
        IOptions<AuthOptions> authOptions,
        StaffTwoFactorService twoFactor,
        ILogger<VerifyCodeCommandHandler> logger,
        AuditEventService eventService,
        IConnectionMultiplexer? redis = null,
        NotificationDispatcher? notifications = null,
        QaTestAccess.QaTestAccessService? qaTestAccess = null,
        FuelFlowMetrics? metrics = null)
    {
        _context = context;
        _tokenService = tokenService;
        _phoneNumberService = phoneNumberService;
        _jwtOptions = jwtOptions.Value;
        // A missing options object must not turn the second factor off: the defaults are the safe
        // ones (StaffSecondFactorRequired is true), so falling back to them fails towards a
        // stricter sign-in rather than a looser one.
        _authOptions = authOptions.Value ?? new AuthOptions();
        _twoFactor = twoFactor;
        _logger = logger;
        _eventService = eventService;
        _redis = redis;
        _notifications = notifications;
        _qaTestAccess = qaTestAccess;
        _metrics = metrics;
    }

    /// <param name="allowRegistration">
    /// When false, an unknown phone is rejected instead of being auto-registered as a new
    /// "User". The admin-login path passes false: an admin sign-in must never create an
    /// account, and the caller has already confirmed the phone belongs to a staff user, so a
    /// missing row here means a race (deleted mid-flow) and must fail closed. The mobile path
    /// leaves this true - self-registration is its intended behaviour.
    /// </param>
    /// <param name="deviceBindingPending">
    /// True when the caller declared itself the mobile app, which replaces this session with the
    /// device-bound one the signed challenge mints. The refresh token minted here is then a
    /// throwaway: it is revoked by that challenge and never outlives the binding window. A
    /// browser - and build 236, which sends no platform header - keeps a real session here.
    /// </param>
    public async Task<VerifyCodeResponse> HandleAsync(VerifyCodeCommand command, CancellationToken cancellationToken, bool allowRegistration = true, bool deviceBindingPending = false)
    {
        var phoneNumber = _phoneNumberService.Normalize(command.PhoneNumber);
        var code = command.Code.Trim();

        // QA gate, re-checked here (not only at send-code) to close the race where a QA code was
        // issued while the switch was on and then submitted after an admin turned it off. If this
        // is the QA phone and QA access is not currently active, reject with the same generic
        // message any wrong code gets — no oracle, and the outstanding QA code (already invalidated
        // by the disable transition) cannot be redeemed. Normal phones skip this entirely.
        if (_qaTestAccess is not null
            && _qaTestAccess.IsQaPhone(phoneNumber)
            && !await _qaTestAccess.IsActiveAsync(cancellationToken))
        {
            _logger.LogWarning("QA verify rejected: QA test-access is not currently enabled");
            _metrics?.QaTestAccessAuthBlocked("disabled");
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        // Load the newest active code regardless of whether it matches, so
        // wrong guesses can be counted against it.
        var verificationCode = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == phoneNumber && !v.IsUsed && v.ExpiresAtUtc > SystemClock.UtcNow)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode == null)
        {
            _logger.LogWarning("Invalid or expired verification code for {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            await LogFailedAdminLoginAsync(phoneNumber, "Invalid or expired verification code", cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        // A row written before the pepper existed can never match a submitted code again: the
        // old digest is an unprefixed SHA-256 of a six-digit value, which is searchable offline.
        // Burn it rather than counting a guess against it - no answer could ever be right, and
        // leaving it live would keep the searchable digest in the table. The migration that
        // introduced the pepper also marks every existing row used; this is the runtime half,
        // so a row that missed the migration (a restored dump, a manual insert) cannot linger.
        if (!OtpCodeHasher.IsPeppered(verificationCode.Code))
        {
            verificationCode.IsUsed = true;
            verificationCode.UsedAtUtc = SystemClock.UtcNow;
            _context.VerificationCodes.Update(verificationCode);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Refused a verification code stored before the OTP pepper existed for {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            await LogFailedAdminLoginAsync(phoneNumber, "Invalid or expired verification code", cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        if (!OtpCodeHasher.Verify(verificationCode.Code, code, _authOptions.OtpPepper))
        {
            verificationCode.FailedAttempts++;
            if (verificationCode.FailedAttempts >= MaxFailedAttempts)
            {
                // Invalidate the code so it cannot be brute-forced further;
                // the user must request a new one (rate-limited per IP).
                verificationCode.IsUsed = true;
                verificationCode.UsedAtUtc = SystemClock.UtcNow;
                _logger.LogWarning(
                    "Verification code invalidated after {MaxAttempts} failed attempts for {PhoneNumber}",
                    MaxFailedAttempts,
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            }

            _context.VerificationCodes.Update(verificationCode);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Invalid verification code for {PhoneNumber} (attempt {Attempt}/{MaxAttempts})",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber), verificationCode.FailedAttempts, MaxFailedAttempts);
            await LogFailedAdminLoginAsync(phoneNumber, "Invalid or expired verification code", cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        verificationCode.IsUsed = true;
        verificationCode.UsedAtUtc = SystemClock.UtcNow;
        _context.VerificationCodes.Update(verificationCode);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && !u.IsDeleted && !u.IsBanned, cancellationToken);

        bool isNewUser = false;
        if (user == null)
        {
            // Check if banned user exists with this phone - reject if so
            var bannedUser = await _context.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber && u.IsBanned, cancellationToken);
            if (bannedUser != null)
            {
                _logger.LogWarning("Banned user attempted to register/login with phone {PhoneNumber}",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                throw new UnauthorizedAccessException("Account is banned");
            }

            if (!allowRegistration)
            {
                // Admin-login path: never mint an account from a sign-in. The code was already
                // burned above (marked used), so a retry cannot resurrect this attempt either.
                _logger.LogWarning("Login attempt for unknown phone {PhoneNumber} rejected (registration not allowed on this path)",
                    SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
                await _context.SaveChangesAsync(cancellationToken);
                throw new UnauthorizedAccessException("Invalid or expired verification code");
            }

            // Deleting an account and signing up again mints a brand-new row, and nothing about
            // the QA identity came with it: the flag and the active state had only ever been set
            // by the seeding migration. So the QA phone re-registering itself came back as an
            // ordinary dormant account - the QA code still worked and the tester got a valid
            // token, so the failure was silent and looked like an empty store
            // (PackageStockAvailability gates the QA catalogue on IsQaAccount) and a checkout that
            // 403s (is_active=false users can use everything EXCEPT payments). That is precisely
            // the path App Review is told to perform, so derive both properties from the same
            // switch-gated condition on every registration rather than leaving them to a one-off
            // migration that cannot run again - the old row is soft-deleted, so it matches
            // "phone_number AND NOT is_deleted" no more.
            //
            // Gated on the same switch the code path above already checked, so QA status can only
            // ever be granted while QA access is actually enabled. A reviewer arriving after the
            // switch was flipped OFF cannot register at all - the gate above rejects the QA phone
            // before the code is even compared - so a late sign-in can neither mint an ordinary
            // active customer nor recover the real-stock catalogue. This is unreachable via the
            // Auth:DevBypass 000000 code: DevBypass only substitutes the SMS code, and it does not
            // get past that gate either.
            var isQaRegistration = _qaTestAccess is not null
                                   && _qaTestAccess.IsQaPhone(phoneNumber)
                                   && await _qaTestAccess.IsActiveAsync(cancellationToken);

            user = new User
            {
                Id = Guid.CreateVersion7(),
                PhoneNumber = phoneNumber,
                // Self-registration is the mobile-app path: stamp the built-in "User"
                // role. Admins are created/maintained by promoting an account to "Admin".
                RoleId = SeedRoles.UserRoleId,
                // Staff activate ordinary accounts as a separate admin/business operation, and
                // OTP verify never does that. The QA account is the one documented exception: it
                // is seeded is_active=true precisely so a reviewer can exercise the full workflow
                // including a test purchase, so a re-registered QA account has to come back the
                // same way the seeded one did.
                IsActive = isQaRegistration,
                IsQaAccount = isQaRegistration,
                CreatedAtUtc = SystemClock.UtcNow
            };
            _context.Users.Add(user);
            isNewUser = true;
        }

        // A staff account does not get a session from the first factor alone. The mailbox is the
        // weakest channel the platform has - it is the one an attacker can influence and the one
        // a phishing campaign reaches - and the staff tier can move money. So the sign-in stops
        // here and continues at the second factor: the client is handed a challenge and comes
        // back with a code from the authenticator app.
        //
        // This runs on EVERY path that can mint a staff session, not only the admin one: the
        // shared /verify endpoint resolves the same account, and without this it would hand a
        // staff token to whoever proved possession of the mailbox.
        if (SeedRoles.IsStaff(user.Role?.Name) && IsSecondFactorRequired())
        {
            // The OTP is spent by now, and the burn has to be committed: a challenge is not a
            // licence to try the same code again.
            await _context.SaveChangesAsync(cancellationToken);

            var challenge = await _twoFactor.IssueChallengeAsync(user, cancellationToken);

            await RecordSecondFactorPendingAsync(user, challenge, cancellationToken);

            return new VerifyCodeResponse(
                null,
                null,
                0,
                string.Empty,
                new TwoFactorChallengeResponse(
                    challenge.Status == TwoFactorStatus.Enroll ? "enroll" : "required",
                    challenge.Challenge,
                    challenge.EnrollmentSecret,
                    challenge.ProvisioningUri));
        }

        return await IssueSessionAsync(user, isNewUser, cancellationToken, deviceBindingPending);
    }

    /// <summary>
    /// Whether a staff sign-in has to prove a second factor.
    /// </summary>
    /// <remarks>
    /// <see cref="AuthOptions.DevBypass"/> switches it off together with everything else: it
    /// already means "this process is not a real environment" - the OTP is a constant, the rate
    /// limits are gone and the SMS never leaves the machine. Demanding an authenticator app in
    /// that environment would mean the development loop needs a phone.
    /// </remarks>
    private bool IsSecondFactorRequired() =>
        _authOptions.StaffSecondFactorRequired && !_authOptions.DevBypass;

    /// <summary>
    /// Records that the first factor of a staff sign-in was proven and the second factor is
    /// outstanding. A completed login is recorded when the session is minted; this is the other
    /// half of the story - an OTP that was accepted for an account that can move money, without
    /// a login behind it.
    /// </summary>
    private async Task RecordSecondFactorPendingAsync(
        User user,
        StaffTwoFactorChallenge challenge,
        CancellationToken cancellationToken)
    {
        var roleName = user.Role?.Name;

        _eventService.RecordEvent(
            "Auth",
            user.Id.ToString(),
            "StaffSecondFactorPending",
            null,
            JsonSerializer.Serialize(new { user.Id, user.PhoneNumber, Role = roleName, Status = challenge.Status.ToString() }),
            user.Id,
            user.PhoneNumber,
            $"{roleName} proved the first factor; the second factor is outstanding ({user.PhoneNumber})",
            user.Id.ToString());

        // An audit row is committed on its own: no business write shares this transaction.
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Mints the session for an account whose first factor has been proven: the access token,
    /// the refresh token, the device registration nonce and the staff-login audit row.
    /// </summary>
    /// <remarks>
    /// Public because the second factor completes the same way the first one started: the
    /// staff sign-in is verified in two steps, and the step that proves the second factor hands
    /// the account over to exactly this code, so both steps mint the same session in the same
    /// shape.
    /// </remarks>
    public async Task<VerifyCodeResponse> IssueSessionAsync(
        User user,
        bool isNewUser,
        CancellationToken cancellationToken,
        bool deviceBindingPending = false)
    {
        // OTP verification registers/logs in the user but does NOT activate them.
        // is_active=false users can use all features EXCEPT payments.
        // Activation (is_active=true) is a separate admin/business operation, with the QA
        // account granted at creation above as the single exception.
        // No change to user.IsActive for an account that already existed.

        user.LastLoginAtUtc = SystemClock.UtcNow;
        if (!isNewUser)
        {
            _context.Users.Update(user);
        }

        // For new users, save the user first so the FK exists for refresh_token
        if (isNewUser)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        var accessToken = _tokenService.GenerateAccessToken(user.Id, user.PhoneNumber, user.Role?.Name, user.FirstName, user.LastName, user.TokenVersion);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();

        var purpose = deviceBindingPending ? RefreshTokenPurpose.DeviceBinding : RefreshTokenPurpose.Session;
        var issuedAt = SystemClock.UtcNow;
        var expiresAt = purpose == RefreshTokenPurpose.DeviceBinding
            ? issuedAt.Add(RefreshTokenPurposePolicy.DeviceBindingWindow)
            : issuedAt.AddDays(_jwtOptions.RefreshTokenExpirationDays);

        var refreshToken = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            // Deliberately Guid.NewGuid(), not CreateVersion7(): this sits inside the refresh-token
        // rotation machinery, and v7 embeds a creation timestamp. Never v4-for-no-reason either -
        // family_id is an indexed grouping key matched only server-side, never a credential.
        FamilyId = Guid.NewGuid(),
            DeviceId = null, // Device will be linked during challenge verification
            RoleNameAtIssue = user.Role?.Name, // Pin the session to the role it was born with (§16)
            Purpose = purpose,
            Token = SecretsHasher.Hash(refreshTokenValue),
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = issuedAt,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);

        if (user.IsQaAccount)
        {
            // Visibility that the QA identity was used, without logging any credential. If the QA
            // account authenticates while the feature is supposed to be off this is the signal
            // (paired with the disabled-block counter) that something is misconfigured.
            _logger.LogInformation("QA test account authenticated");
            _metrics?.QaTestAccessAuthSucceeded();
        }

        // Sent only after SaveChanges so a message can never describe a user that was
        // not actually persisted.
        if (isNewUser && _notifications is not null)
        {
            await _notifications.NewUserRegisteredAsync(user.Id, user.PhoneNumber, cancellationToken);
        }

        // Audit EVERY staff login, not just literal "Admin". The old gate
        // (user.Role?.Name == "Admin") left ProductOwner and Manager logins
        // unaudited even though the failed-login auditor (LogFailedAdminLoginAsync)
        // already covers all three staff roles - so a failed PO login was recorded
        // but a successful one was not. Gate on SeedRoles.IsStaff and carry the
        // actual role so PO/Manager/Admin are distinguishable in the audit trail.
        var roleName = user.Role?.Name;
        if (SeedRoles.IsStaff(roleName))
        {
            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _eventService.RecordEvent(
                "Auth",
                user.Id.ToString(),
                "StaffLoggedIn",
                null,
                JsonSerializer.Serialize(new { user.Id, user.PhoneNumber, Role = roleName }),
                user.Id,
                string.IsNullOrWhiteSpace(displayName) ? user.PhoneNumber : displayName,
                $"{roleName} logged in ({user.PhoneNumber})",
                user.Id.ToString());

            // A login is not a change to anything else, so there is no business write to share a
            // transaction with: the row is committed on its own. The audit service does not save, so
            // the caller does (planning #533).
            await _context.SaveChangesAsync(cancellationToken);
        }

        return new VerifyCodeResponse(
            accessToken,
            refreshTokenValue,
            _jwtOptions.AccessTokenExpirationMinutes * 60,
            await IssueDeviceRegistrationNonceAsync(user, cancellationToken)
        );
    }

    /// <summary>
    /// Issues the one-time device registration nonce consumed by the register-device
    /// endpoint when it needs to rebind a device_id enrolled by a different user.
    /// Best effort: if Redis is unavailable login still succeeds - the caller simply
    /// cannot rebind in this session and will get the plain DeviceAlreadyRegistered
    /// refusal, exactly as before.
    /// </summary>
    private async Task<string> IssueDeviceRegistrationNonceAsync(User user, CancellationToken cancellationToken)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        if (_redis is null)
            return nonce;

        try
        {
            await _redis.GetDatabase().StringSetAsync(
                (RedisKey)$"{RegistrationNonceKeyPrefix}{user.Id:N}:{nonce}",
                "1",
                RegistrationNonceTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist device registration nonce for user {UserId}", user.Id);
        }

        return nonce;
    }

    private async Task LogFailedAdminLoginAsync(string phoneNumber, string reason, CancellationToken cancellationToken)
    {
        var staffRoleNames = SeedRoles.StaffRoleNames;
        
        var adminUser = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber 
                && u.Role != null 
                && staffRoleNames.Contains(u.Role.Name) 
                && !u.IsDeleted, cancellationToken);

        if (adminUser == null) return;

        _eventService.RecordEvent(
            "Auth",
            adminUser.Id.ToString(),
            "AdminLoginFailed",
            null,
            JsonSerializer.Serialize(new { adminUser.Id, phoneNumber, reason }),
            adminUser.Id,
            adminUser.PhoneNumber,
            $"Failed admin login for {phoneNumber} ({reason})",
            adminUser.Id.ToString());

        // A failed login changes nothing else, so the row is committed on its own. The audit
        // service does not save, so the caller does (planning #533).
        await _context.SaveChangesAsync(cancellationToken);
    }
}
