using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.VerifyChallenge;
using FuelFlow.SharedKernel.Options;
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
    private readonly LogoutEverywhereCommandHandler _logoutEverywhereHandler;
    private readonly AuthOptions _authOptions;

    public DeviceAuthController(
        RegisterDeviceCommandHandler registerDeviceHandler,
        GenerateChallengeCommandHandler generateChallengeHandler,
        VerifyChallengeCommandHandler verifyChallengeHandler,
        LogoutDeviceCommandHandler logoutDeviceHandler,
        LogoutEverywhereCommandHandler logoutEverywhereHandler,
        IOptions<AuthOptions> authOptions)
    {
        _registerDeviceHandler = registerDeviceHandler;
        _generateChallengeHandler = generateChallengeHandler;
        _verifyChallengeHandler = verifyChallengeHandler;
        _logoutDeviceHandler = logoutDeviceHandler;
        _logoutEverywhereHandler = logoutEverywhereHandler;
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
            return BadRequest(new { error = new { message = "DeviceId is required" } });

        if (string.IsNullOrWhiteSpace(command.PublicKey))
            return BadRequest(new { error = new { message = "PublicKey is required" } });

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { error = new { message = "Invalid user token" } });

        command.UserId = userId;

        var result = await _registerDeviceHandler.HandleAsync(command, cancellationToken);

        if (result.Error is not null)
            return Conflict(new { error = new { message = result.Error } });

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
            return BadRequest(new { error = new { message = "DeviceId is required" } });

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
            return BadRequest(new { error = new { message = "DeviceId is required" } });

        if (string.IsNullOrWhiteSpace(command.Challenge))
            return BadRequest(new { error = new { message = "Challenge is required" } });

        if (string.IsNullOrWhiteSpace(command.Signature))
            return BadRequest(new { error = new { message = "Signature is required" } });

        var result = await _verifyChallengeHandler.HandleAsync(command, cancellationToken);

        if (!result.IsValid)
            return Unauthorized(new { error = new { message = result.Error } });

        return Ok(result);
    }

    /// <remarks>
    /// Signature-verification diagnostics for onboarding a new device key format. Returns 404 unless
    /// <c>Auth:DevBypass</c> is set, which <c>ValidateSecurityConfiguration</c> refuses in Production
    /// (Program.cs:193-200), so this is unreachable in production regardless of the anonymous
    /// attribute. Anonymous by design: the whole point is to test a key that is not yet registered.
    /// </remarks>
    [HttpPost("verify-raw")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult VerifyRaw(
        [FromBody] VerifyRawRequest request)
    {
        if (!_authOptions.DevBypass)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Challenge))
            return BadRequest(new { error = "Challenge is required" });
        if (string.IsNullOrWhiteSpace(request.Signature))
            return BadRequest(new { error = "Signature is required" });
        if (string.IsNullOrWhiteSpace(request.PublicKey))
            return BadRequest(new { error = "PublicKey is required" });

        var challengeBytes = Encoding.UTF8.GetBytes(request.Challenge);
        byte[] signatureBytes;
        try { signatureBytes = Convert.FromBase64String(request.Signature); }
        catch { return BadRequest(new { error = "Invalid base64 signature" }); }

        // Normalize PEM
        string rawKey = request.PublicKey.Trim();
        string pemKey;
        if (rawKey.StartsWith("-----"))
        {
            pemKey = rawKey;
        }
        else
        {
            // Strip any whitespace/newlines from the raw base64 blob
            var base64Only = new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray());
            pemKey = $"-----BEGIN PUBLIC KEY-----\n{base64Only}\n-----END PUBLIC KEY-----";
        }

        // Compute a fingerprint of the key for diagnostics
        byte[] keyBytes;
        try { keyBytes = Convert.FromBase64String(new string(rawKey.Where(c => !char.IsWhiteSpace(c)).ToArray())); }
        catch { keyBytes = []; }
        var keyFingerprint = keyBytes.Length > 0
            ? Convert.ToHexString(SHA256.HashData(keyBytes))[..16]
            : "invalid-base64";

        var diag = new Dictionary<string, object>
        {
            ["challengeLen"] = challengeBytes.Length,
            ["signatureLen"] = signatureBytes.Length,
            ["keyLen"] = rawKey.Length,
            ["keyFingerprint"] = keyFingerprint,
        };

        // --- Try RSA PKCS1 (iOS react-native-biometrics uses RSA-2048 + PKCS1v15 SHA256) ---
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pemKey);
            diag["keyType"] = "RSA";
            diag["rsaKeySize"] = rsa.KeySize;
            bool valid = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            diag["rsaPkcs1Valid"] = valid;
            // Also try PSS just in case
            bool pssValid = false;
            try { pssValid = rsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); } catch { }
            diag["rsaPssValid"] = pssValid;
            diag["valid"] = valid || pssValid;
            diag["method"] = valid ? "RSA-PKCS1" : (pssValid ? "RSA-PSS" : "RSA-FAILED");
            return Ok(diag);
        }
        catch (CryptographicException ex)
        {
            diag["rsaImportError"] = ex.Message;
        }

        // --- Try ECDSA (Android or custom key type) ---
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pemKey);
            diag["keyType"] = "ECDSA";

            bool derValid = false;
            bool ieeeValid = false;
            string? derErr = null, ieeeErr = null;

            try { derValid = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence); }
            catch (CryptographicException e) { derErr = e.Message; }

            try { ieeeValid = ecdsa.VerifyData(challengeBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation); }
            catch (CryptographicException e) { ieeeErr = e.Message; }

            diag["ecdsaDerValid"] = derValid;
            diag["ecdsaIeeeValid"] = ieeeValid;
            if (derErr != null) diag["ecdsaDerError"] = derErr;
            if (ieeeErr != null) diag["ecdsaIeeeError"] = ieeeErr;
            diag["valid"] = derValid || ieeeValid;
            diag["method"] = derValid ? "ECDSA-DER" : (ieeeValid ? "ECDSA-IEEE" : "ECDSA-FAILED");
            return Ok(diag);
        }
        catch (Exception ex)
        {
            diag["ecdsaImportError"] = ex.Message;
            diag["valid"] = false;
            diag["method"] = "KEY-IMPORT-FAILED";
            return Ok(diag);
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user token");

        var deviceId = Request.Headers["x-device-id"].FirstOrDefault() ?? string.Empty;

        await _logoutDeviceHandler.HandleAsync(
            new LogoutDeviceCommand(deviceId, userId), cancellationToken);

        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("logout-everywhere")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutEverywhere(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user token");

        await _logoutEverywhereHandler.HandleAsync(
            new LogoutEverywhereCommand(userId), cancellationToken);

        return Ok(new { message = "Logged out from all devices" });
    }
}
