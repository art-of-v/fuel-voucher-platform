using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
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
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            DeviceId = command.DeviceId,
            // Store the HASH, exactly like the standard refresh flow does — /api/auth/refresh
            // looks tokens up by SHA-256, so a raw value here is silently unfetchable and every
            // device session dies at first refresh with "Invalid or expired refresh token".
            Token = SecretsHasher.Hash(refreshToken),
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

        // Convert.FromBase64String throws on malformed input, and this method is called
        // outside any try/catch, so a client-supplied non-base64 signature produced an
        // unhandled FormatException - a 500 and an error-log row instead of a 401, on an
        // anonymous endpoint. A signature that is not base64 is simply an invalid signature.
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Signature is not valid base64");
            return false;
        }

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

        // These traces are Debug, not Information. They were added to diagnose the mobile
        // signing format against verify-raw and they run on an anonymous endpoint, printing
        // key fingerprints, key/signature lengths and per-algorithm outcomes on every attempt.
        // None of it is secret, but it is high-volume detail about the credential-minting path
        // that anyone with log read access can mine, and Information is the level that ships.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            byte[] keyBytes;
            try { keyBytes = Convert.FromBase64String(new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray())); }
            catch { keyBytes = []; }
            var keyFingerprint = keyBytes.Length > 0
                ? Convert.ToHexString(SHA256.HashData(keyBytes))[..16]
                : "invalid-base64";

            _logger.LogDebug(
                "VerifySignature: challengeLen={CLen} signatureLen={SLen} keyLen={KLen} keyFingerprint={KF}",
                challengeBytes.Length, signatureBytes.Length, rawKey.Length, keyFingerprint);
        }

        // --- RSA PKCS1v15 SHA256 (react-native-biometrics iOS default) ---
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
            _logger.LogDebug("Key imported as RSA-{Size}", rsa.KeySize);
            var result = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _logger.LogDebug("RSA PKCS1 result: {Result}", result);
            if (result) return true;

            // Also try PSS
            try
            {
                var pss = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
                _logger.LogDebug("RSA PSS result: {Result}", pss);
                if (pss) return true;
            }
            catch { }
        }
        catch (CryptographicException ex)
        {
            _logger.LogDebug("RSA import/verify failed: {Msg}", ex.Message);
        }

        // --- ECDSA fallback (Android or custom key) ---
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pemKey);
            _logger.LogDebug("Key imported as ECDSA");

            bool derResult = false;
            try
            {
                derResult = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                _logger.LogDebug("ECDSA DER result: {R}", derResult);
            }
            catch (CryptographicException ex) { _logger.LogDebug("ECDSA DER error: {M}", ex.Message); }

            if (derResult) return true;

            bool ieeeResult = false;
            try
            {
                ieeeResult = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                _logger.LogDebug("ECDSA IEEE result: {R}", ieeeResult);
            }
            catch (CryptographicException ex) { _logger.LogDebug("ECDSA IEEE error: {M}", ex.Message); }

            return ieeeResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All signature verification methods failed: {Message}", ex.Message);
            return false;
        }
    }
}
