using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
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

        _logger.LogInformation(
            "Verifying signature for device {DeviceId}: challenge={ChallengePreview}, publicKeyPreview={KeyPreview}",
            command.DeviceId,
            command.Challenge[..Math.Min(command.Challenge.Length, 16)],
            device.PublicKey[..Math.Min(device.PublicKey.Length, 64)]);

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
        _context.Devices.Update(device);
        await _context.SaveChangesAsync(cancellationToken);

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == device.UserId, cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.UserId,
            user?.PhoneNumber ?? string.Empty,
            user?.Role?.Name,
            user?.FirstName,
            user?.LastName);
        var refreshToken = _tokenService.GenerateRefreshToken();

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = device.UserId,
            Token = refreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAtUtc = DateTime.UtcNow,
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

    private bool VerifySignature(string challenge, string signatureBase64, string publicKeyPem)
    {
        var challengeBytes = Encoding.UTF8.GetBytes(challenge);
        var signatureBytes = Convert.FromBase64String(signatureBase64);

        // Normalize: if not a PEM block, strip ALL whitespace from the base64 and wrap
        string rawKey = publicKeyPem.Trim();
        string pemKey;
        if (rawKey.StartsWith("-----"))
        {
            pemKey = rawKey;
        }
        else
        {
            var base64Only = new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray());
            pemKey = $"-----BEGIN PUBLIC KEY-----\n{base64Only}\n-----END PUBLIC KEY-----";
        }

        // Log a fingerprint of the stored key for diagnostic comparison vs verify-raw
        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray())); }
        catch { keyBytes = []; }
        var keyFingerprint = keyBytes.Length > 0
            ? Convert.ToHexString(SHA256.HashData(keyBytes))[..16]
            : "invalid-base64";

        _logger.LogInformation(
            "VerifySignature: challengeLen={CLen} signatureLen={SLen} keyLen={KLen} keyFingerprint={KF}",
            challengeBytes.Length, signatureBytes.Length, rawKey.Length, keyFingerprint);

        // --- RSA PKCS1v15 SHA256 (react-native-biometrics iOS default) ---
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
            _logger.LogInformation("Key imported as RSA-{Size}", rsa.KeySize);
            var result = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _logger.LogInformation("RSA PKCS1 result: {Result}", result);
            if (result) return true;

            // Also try PSS
            try
            {
                var pss = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                _logger.LogInformation("RSA PSS result: {Result}", pss);
                if (pss) return true;
            }
            catch { }
        }
        catch (CryptographicException ex)
        {
            _logger.LogInformation("RSA import/verify failed: {Msg}", ex.Message);
        }

        // --- ECDSA fallback (Android or custom key) ---
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pemKey);
            _logger.LogInformation("Key imported as ECDSA");

            bool derResult = false;
            try
            {
                derResult = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                _logger.LogInformation("ECDSA DER result: {R}", derResult);
            }
            catch (CryptographicException ex) { _logger.LogInformation("ECDSA DER error: {M}", ex.Message); }

            if (derResult) return true;

            bool ieeeResult = false;
            try
            {
                ieeeResult = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                _logger.LogInformation("ECDSA IEEE result: {R}", ieeeResult);
            }
            catch (CryptographicException ex) { _logger.LogInformation("ECDSA IEEE error: {M}", ex.Message); }

            return ieeeResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All signature verification methods failed: {Message}", ex.Message);
            return false;
        }
    }
}
