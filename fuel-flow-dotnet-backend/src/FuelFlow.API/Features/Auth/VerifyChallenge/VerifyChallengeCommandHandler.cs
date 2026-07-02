using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.SharedAbstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Infrastructure.Caching;
using FuelFlow.Options;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace FuelFlow.Features.Auth.VerifyChallenge;

public sealed class VerifyChallengeCommandHandler
{
    private readonly ApplicationDbContext _context;
    private readonly ICacheService _cacheService;
    private readonly IJwtTokenService _tokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<VerifyChallengeCommandHandler> _logger;

    public VerifyChallengeCommandHandler(
        ApplicationDbContext context,
        ICacheService cacheService,
        IJwtTokenService tokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<VerifyChallengeCommandHandler> logger)
    {
        _context = context;
        _cacheService = cacheService;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<VerifyChallengeResponse> HandleAsync(
        VerifyChallengeCommand command,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Verifying challenge for device {DeviceId}",
            command.DeviceId);

        var cacheKey = $"challenge:{command.DeviceId}";
        var storedChallenge = await _cacheService.GetAsync(cacheKey, cancellationToken);

        if (string.IsNullOrEmpty(storedChallenge))
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

        if (storedChallenge != command.Challenge)
        {
            _logger.LogWarning(
                "Challenge mismatch for device {DeviceId}",
                command.DeviceId);

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "Invalid challenge"
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

        bool isSignatureValid = VerifySignature(
            command.Challenge,
            command.Signature,
            device.PublicKey);

        if (!isSignatureValid)
        {
            _logger.LogWarning(
                "Invalid signature for device {DeviceId}",
                command.DeviceId);

            return new VerifyChallengeResponse
            {
                IsValid = false,
                Error = "Invalid signature"
            };
        }

        await _cacheService.RemoveAsync(cacheKey, cancellationToken);

        device.LastSeenAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == device.UserId, cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.UserId,
            user?.PhoneNumber ?? string.Empty,
            user?.Role?.Name);
        var refreshToken = _tokenService.GenerateRefreshToken();

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

    private bool VerifySignature(string challenge, string signatureBase64, string publicKeyPem)
    {
        try
        {
            var challengeBytes = Encoding.UTF8.GetBytes(challenge);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            using var rsa = RSA.Create();

            // react-native-biometrics returns raw Base64 DER (SPKI) without PEM headers.
            // Wrap in PEM headers if not already present.
            string pemKey = publicKeyPem.Trim();
            if (!pemKey.StartsWith("-----"))
            {
                    pemKey = pemKey.Replace("\r", "").Replace("\n", "").Replace(" ", "");
                pemKey = $"-----BEGIN PUBLIC KEY-----\n{pemKey}\n-----END PUBLIC KEY-----";
            }

            rsa.ImportFromPem(pemKey);

            // Verify signature using SHA256 with PKCS1 (react-native-biometrics default)
            return rsa.VerifyData(
                challengeBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error verifying signature: {Message}",
                ex.Message);
            return false;
        }
    }
}
