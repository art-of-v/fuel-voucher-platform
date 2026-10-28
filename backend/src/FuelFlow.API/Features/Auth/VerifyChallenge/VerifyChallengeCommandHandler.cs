using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Auth.VerifyChallenge;

public sealed class VerifyChallengeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly IJwtTokenService _tokenService;
    private readonly DeviceSignatureVerifier _signatureVerifier;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<VerifyChallengeCommandHandler> _logger;

    public VerifyChallengeCommandHandler(
        ApplicationDbContext context,
        ICacheService cacheService,
        IJwtTokenService tokenService,
        DeviceSignatureVerifier signatureVerifier,
        IOptions<JwtOptions> jwtOptions,
        ILogger<VerifyChallengeCommandHandler> logger)
    {
        _context = context;
        _cacheService = cacheService;
        _tokenService = tokenService;
        _signatureVerifier = signatureVerifier;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<VerifyChallengeResponse> HandleAsync(
        VerifyChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Verifying challenge for device {DeviceId}",
            command.DeviceId);

        // Keyed by the challenge value, so presence of the key IS proof that this server issued
        // this exact challenge to this device. That replaces the previous fetch-then-compare
        // against a single per-device slot, which a third party could overwrite - see
        // GenerateChallengeCommandHandler.CacheKey.
        var cacheKey = GenerateChallengeCommandHandler.CacheKey(command.DeviceId, command.Challenge);
        var challengeWasIssued = await _cacheService.ExistsAsync(cacheKey, cancellationToken);

        if (!challengeWasIssued)
        {
            _logger.LogWarning(
                "Challenge not found or expired for device {DeviceId}",
                command.DeviceId);

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "Challenge not found or expired"
            };
        }

        var device = await _context.Devices
            .FirstOrDefaultAsync(
                d => d.DeviceId == command.DeviceId && d.Status == DeviceStatus.Active,
                cancellationToken);

        if (device == null)
        {
            _logger.LogWarning(
                "Device {DeviceId} not found or not active",
                command.DeviceId);

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "Device not found or revoked"
            };
        }

        // Debug, and without the key material: this ran at Information on an anonymous endpoint,
        // printing a challenge preview and the first 64 characters of the stored public key on
        // every attempt.
        _logger.LogDebug(
            "Verifying signature for device {DeviceId}",
            command.DeviceId);

        var verification = _signatureVerifier.VerifyChallenge(
            device,
            command.DeviceId,
            command.Challenge,
            command.Signature);

        if (!verification.IsValid)
        {
            _logger.LogWarning(
                "Invalid signature for device {DeviceId} (tried {Algorithms})",
                command.DeviceId,
                string.Join(",", verification.Attempts.Select(a => a.Algorithm)));

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "Invalid signature"
            };
        }

        await _cacheService.RemoveAsync(cacheKey, cancellationToken);

        // A row enrolled before the pins existed has no scheme recorded. The signature just proved
        // possession, so pin whatever verified rather than leaving the fallback in place forever.
        if (device.SignatureAlgorithm is null && verification.MatchedAlgorithm is { } matched)
        {
            device.SignatureAlgorithm = matched;
            _logger.LogInformation(
                "Pinned legacy device {DeviceId} to {Algorithm}",
                command.DeviceId,
                matched);
        }

        // Same for the payload shape: a device whose bundle changed what it signs must be able to
        // sign in to refresh its row, so the shape that verified is recorded here. Without this the
        // stale pin would reject every later request while the login that fixes it stays refused.
        if (verification.MatchedPayloadVersion is { } matchedVersion
            && matchedVersion != device.SignaturePayloadVersion)
        {
            _logger.LogInformation(
                "Re-pinned device {DeviceId} from payload v{OldVersion} to v{NewVersion}",
                command.DeviceId,
                device.SignaturePayloadVersion,
                matchedVersion);

            device.SignaturePayloadVersion = matchedVersion;
        }

        device.LastSeenAt = SystemClock.UtcNow;
        _context.Devices.Update(device);
        await _context.SaveChangesAsync(cancellationToken);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == device.UserId, cancellationToken);

        if (user == null || user.IsBanned)
        {
            _logger.LogWarning(
                "Challenge verify rejected: user {UserId} not found or banned for device {DeviceId}",
                device.UserId,
                command.DeviceId);

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "User not found"
            };
        }

        var accessToken = _tokenService.GenerateAccessToken(
            user.Id,
            user.PhoneNumber,
            user.Role?.Name,
            user.FirstName,
            user.LastName,
            user.TokenVersion);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            // Guid.NewGuid(), not CreateVersion7() - see VerifyCodeCommand: this is part of the
        // refresh-token rotation family, where an id should not carry a creation timestamp.
        FamilyId = Guid.NewGuid(),
            DeviceId = command.DeviceId,
            RoleNameAtIssue = user.Role?.Name, // Pin the session to the role it was born with (§16)
            // Store the HASH, exactly like the standard refresh flow does — /api/auth/refresh
            // looks tokens up by SHA-256, so a raw value here is silently unfetchable and every
            // device session dies at first refresh with "Invalid or expired refresh token".
            Token = SecretsHasher.Hash(refreshToken),
            ExpiresAtUtc = SystemClock.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = SystemClock.UtcNow,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Challenge verified successfully for device {DeviceId}, user {UserId}",
            command.DeviceId,
            device.UserId);

        return new VerifyChallengeResponse
        {
            IsValid = true,
            UserId = device.UserId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _jwtOptions.AccessTokenExpirationMinutes * 60
        };
    }
}
