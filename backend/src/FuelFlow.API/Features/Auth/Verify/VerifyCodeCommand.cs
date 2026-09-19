using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text.Json;

namespace FuelFlow.Features.Auth.Verify;

public sealed record VerifyCodeCommand(string PhoneNumber, string Code);

public sealed record VerifyCodeResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string DeviceRegistrationNonce
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
    private readonly ILogger<VerifyCodeCommandHandler> _logger;
    private readonly ProviderEventService _eventService;
    private readonly NotificationDispatcher? _notifications;
    private readonly IConnectionMultiplexer? _redis;

    public VerifyCodeCommandHandler(
        ApplicationDbContext context,
        IJwtTokenService tokenService,
        IPhoneNumberService phoneNumberService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<VerifyCodeCommandHandler> logger,
        ProviderEventService eventService,
        IConnectionMultiplexer? redis = null,
        NotificationDispatcher? notifications = null)
    {
        _context = context;
        _tokenService = tokenService;
        _phoneNumberService = phoneNumberService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
        _eventService = eventService;
        _redis = redis;
        _notifications = notifications;
    }

    /// <param name="allowRegistration">
    /// When false, an unknown phone is rejected instead of being auto-registered as a new
    /// "User". The admin-login path passes false: an admin sign-in must never create an
    /// account, and the caller has already confirmed the phone belongs to a staff user, so a
    /// missing row here means a race (deleted mid-flow) and must fail closed. The mobile path
    /// leaves this true - self-registration is its intended behaviour.
    /// </param>
    public async Task<VerifyCodeResponse> HandleAsync(VerifyCodeCommand command, CancellationToken cancellationToken, bool allowRegistration = true)
    {
        var phoneNumber = _phoneNumberService.Normalize(command.PhoneNumber);
        var code = command.Code.Trim();

        // Load the newest active code regardless of whether it matches, so
        // wrong guesses can be counted against it.
        var verificationCode = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == phoneNumber && !v.IsUsed && v.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificationCode == null)
        {
            _logger.LogWarning("Invalid or expired verification code for {PhoneNumber}",
                SensitiveDataRedactor.MaskPhoneNumber(phoneNumber));
            await LogFailedAdminLoginAsync(phoneNumber, "Invalid or expired verification code", cancellationToken);
            throw new UnauthorizedAccessException("Invalid or expired verification code");
        }

        if (verificationCode.Code != SecretsHasher.Hash(code))
        {
            verificationCode.FailedAttempts++;
            if (verificationCode.FailedAttempts >= MaxFailedAttempts)
            {
                // Invalidate the code so it cannot be brute-forced further;
                // the user must request a new one (rate-limited per IP).
                verificationCode.IsUsed = true;
                verificationCode.UsedAtUtc = DateTime.UtcNow;
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
        verificationCode.UsedAtUtc = DateTime.UtcNow;
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

            user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                // Self-registration is the mobile-app path: stamp the built-in "User"
                // role. Admins are created/maintained by promoting an account to "Admin".
                RoleId = SeedRoles.UserRoleId,
                IsActive = false, // Activated only by staff, never by OTP verify (see below)
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.Users.Add(user);
            isNewUser = true;
        }

        // OTP verification registers/logs in the user but does NOT activate them.
        // is_active=false users can use all features EXCEPT payments.
        // Activation (is_active=true) is a separate admin/business operation.
        // No change to user.IsActive here.

        user.LastLoginAtUtc = DateTime.UtcNow;
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

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            DeviceId = null, // Device will be linked during challenge verification
            RoleNameAtIssue = user.Role?.Name, // Pin the session to the role it was born with (§16)
            Token = SecretsHasher.Hash(refreshTokenValue),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} authenticated successfully", user.Id);

        // Sent only after SaveChanges so a message can never describe a user that was
        // not actually persisted.
        if (isNewUser && _notifications is not null)
        {
            await _notifications.NewUserRegisteredAsync(user.Id, user.PhoneNumber, cancellationToken);
        }

        if (user.Role?.Name == "Admin")
        {
            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            await _eventService.RecordEventAsync(
                "Auth",
                user.Id.ToString(),
                "AdminLoggedIn",
                null,
                JsonSerializer.Serialize(new { user.Id, user.PhoneNumber }),
                user.Id,
                string.IsNullOrWhiteSpace(displayName) ? user.PhoneNumber : displayName,
                $"Admin logged in ({user.PhoneNumber})",
                user.Id.ToString(),
                cancellationToken);
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
        var staffRoleNames = new[] { SeedRoles.ProductOwnerName, SeedRoles.AdminName, SeedRoles.ManagerName };
        
        var adminUser = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber 
                && u.Role != null 
                && staffRoleNames.Contains(u.Role.Name) 
                && !u.IsDeleted, cancellationToken);

        if (adminUser == null) return;

        await _eventService.RecordEventAsync(
            "Auth",
            adminUser.Id.ToString(),
            "AdminLoginFailed",
            null,
            JsonSerializer.Serialize(new { adminUser.Id, phoneNumber, reason }),
            adminUser.Id,
            adminUser.PhoneNumber,
            $"Failed admin login for {phoneNumber} ({reason})",
            adminUser.Id.ToString(),
            cancellationToken);
    }
}
