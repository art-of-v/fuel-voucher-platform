using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Auth;

[ApiController]
[Route("api/auth/device")]
public sealed class DeviceAuthController : ControllerBase
{
    private readonly RegisterDeviceCommandHandler _registerDeviceHandler;
    private readonly GenerateChallengeCommandHandler _generateChallengeHandler;
    private readonly VerifyChallengeCommandHandler _verifyChallengeHandler;
    private readonly LogoutDeviceCommandHandler _logoutDeviceHandler;
    private readonly DeviceSignatureVerifier _signatureVerifier;
    private readonly AuthOptions _authOptions;

    public DeviceAuthController(
        RegisterDeviceCommandHandler registerDeviceHandler,
        GenerateChallengeCommandHandler generateChallengeHandler,
        VerifyChallengeCommandHandler verifyChallengeHandler,
        LogoutDeviceCommandHandler logoutDeviceHandler,
        DeviceSignatureVerifier signatureVerifier,
        IOptions<AuthOptions> authOptions)
    {
        _registerDeviceHandler = registerDeviceHandler;
        _generateChallengeHandler = generateChallengeHandler;
        _verifyChallengeHandler = verifyChallengeHandler;
        _logoutDeviceHandler = logoutDeviceHandler;
        _signatureVerifier = signatureVerifier;
        _authOptions = authOptions.Value;
    }

    [HttpPost("register")]
    [Authorize]
    [ProducesResponseType(typeof(RegisterDeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return ApiError.BadRequest("DeviceId is required");

        if (string.IsNullOrWhiteSpace(command.PublicKey))
            return ApiError.BadRequest("PublicKey is required");

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return ApiError.Unauthorized("Invalid user token");

        command.UserId = userId;

        var result = await _registerDeviceHandler.HandleAsync(command, cancellationToken);

        if (result.Error is "DeviceAlreadyRegistered")
            return ApiError.Conflict(result.Error);

        if (result.Error is not null)
            return ApiError.BadRequest(result.Error);

        return Ok(result);
    }

    [HttpPost("challenge")]
    [AllowAnonymous]
    [EnableRateLimiting(DeviceChallengePolicy)]
    [ProducesResponseType(typeof(GenerateChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateChallenge(
        [FromBody] GenerateChallengeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return ApiError.BadRequest("DeviceId is required");

        var result = await _generateChallengeHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting(DeviceVerifyPolicy)]
    [ProducesResponseType(typeof(VerifyChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyChallenge(
        [FromBody] VerifyChallengeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return ApiError.BadRequest("DeviceId is required");

        if (string.IsNullOrWhiteSpace(command.Challenge))
            return ApiError.BadRequest("Challenge is required");

        if (string.IsNullOrWhiteSpace(command.Signature))
            return ApiError.BadRequest("Signature is required");

        var result = await _verifyChallengeHandler.HandleAsync(command, cancellationToken);

        if (!result.IsValid)
            return ApiError.Unauthorized(result.Error);

        // The device flow is the native app's, but the rule is the rule everywhere: the refresh
        // token travels in the body only for a client that says it is native and has no cookie
        // jar to keep it in. See ClientPlatform.
        if (!ClientPlatform.IsNative(Request.Headers))
            result.RefreshToken = null;

        return Ok(result);
    }

    /// <remarks>
    /// Signature-verification diagnostics for onboarding a new device key format. Returns 404 unless
    /// <c>Auth:DevBypass</c> is set, which <c>SecurityConfigurationValidator</c> refuses in Production
    /// (Program.cs:193-200), so this is unreachable in production regardless of the anonymous
    /// attribute. Anonymous by design: the whole point is to test a key that is not yet registered.
    /// <para>
    /// The checking itself is <see cref="DeviceSignatureVerifier.Diagnose"/> - the production
    /// verifier. This endpoint used to carry its own RSA/ECDSA loops, which meant a key format
    /// could pass here and fail at login.
    /// </para>
    /// </remarks>
    [HttpPost("verify-raw")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult VerifyRaw(
        [FromBody] VerifyRawRequest request)
    {
        if (!_authOptions.DevBypass)
            return ApiError.NotFound("Not found");

        if (string.IsNullOrWhiteSpace(request.Challenge))
            return ApiError.BadRequest("Challenge is required");
        if (string.IsNullOrWhiteSpace(request.Signature))
            return ApiError.BadRequest("Signature is required");
        if (string.IsNullOrWhiteSpace(request.PublicKey))
            return ApiError.BadRequest("PublicKey is required");

        var result = _signatureVerifier.Diagnose(
            request.Challenge,
            request.Signature,
            request.PublicKey);

        string rawKey = request.PublicKey.Trim();

        // Compute a fingerprint of the key for diagnostics. This is a SHA-256 of the key bytes,
        // not a verification step; the key itself stays out of the response.
        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray())); }
        catch { keyBytes = []; }
        var keyFingerprint = keyBytes.Length > 0
            ? Convert.ToHexString(SHA256.HashData(keyBytes))[..16]
            : "invalid-base64";

        var diag = new Dictionary<string, object?>
        {
            ["challengeLen"] = Encoding.UTF8.GetByteCount(request.Challenge),
            ["keyLen"] = rawKey.Length,
            ["keyFingerprint"] = keyFingerprint,
            ["keyType"] = result.KeyType ?? "unknown",
            ["valid"] = result.IsValid,
            ["method"] = result.MatchedAlgorithm?.ToString() ?? result.Failure ?? "NO-MATCH",
        };

        try { diag["signatureLen"] = Convert.FromBase64String(request.Signature).Length; }
        catch { diag["signatureLen"] = 0; }

        foreach (var attempt in result.Attempts)
        {
            var name = attempt.Algorithm switch
            {
                AsymmetricSignatureAlgorithm.RsaPkcs1Sha256 => "rsaPkcs1",
                AsymmetricSignatureAlgorithm.RsaPssSha256 => "rsaPss",
                AsymmetricSignatureAlgorithm.EcdsaSha256Der => "ecdsaDer",
                AsymmetricSignatureAlgorithm.EcdsaSha256P1363 => "ecdsaIeee",
                _ => attempt.Algorithm.ToString()
            };

            diag[$"{name}Valid"] = attempt.IsValid;

            if (!attempt.IsValid && attempt.Error is not null)
            {
                diag[$"{name}Error"] = attempt.Error;
            }
        }

        return Ok(diag);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return ApiError.Unauthorized("Invalid user token");

        var deviceId = Request.Headers["x-device-id"].FirstOrDefault() ?? string.Empty;

        await _logoutDeviceHandler.HandleAsync(
            new LogoutDeviceCommand(deviceId, userId), cancellationToken);

        return Ok(new { message = "Logged out successfully" });
    }
}
