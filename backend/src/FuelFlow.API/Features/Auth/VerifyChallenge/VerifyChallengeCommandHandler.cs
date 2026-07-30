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

        string pemKey = publicKeyPem.Trim();
        if (!pemKey.StartsWith("-----"))
        {
            pemKey = pemKey.Replace("\r", "").Replace("\n", "").Replace(" ", "");
            pemKey = $"-----BEGIN PUBLIC KEY-----\n{pemKey}\n-----END PUBLIC KEY-----";
        }

        _logger.LogInformation(
            "RSA VerifyData: challengeBytes={Len} bytes, signatureBytes={SigLen} bytes",
            challengeBytes.Length,
            signatureBytes.Length);

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
            var result = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            _logger.LogInformation("RSA verification result: {Result}", result);
            if (result) return true;
        }
        catch (CryptographicException ex)
        {
            _logger.LogInformation(ex, "RSA import/verify failed, falling back to ECDSA");
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pemKey);
            
            bool result = false;
            try
            {
                result = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            }
            catch (CryptographicException)
            {
            }

            if (!result)
            {
                try
                {
                    result = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                }
                catch (CryptographicException)
                {
                }
            }

            _logger.LogInformation("ECDSA verification result: {Result}", result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "All signature verification methods failed: {Message}", ex.Message);
            return false;
        }
    }
}
