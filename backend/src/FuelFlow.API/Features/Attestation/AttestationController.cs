using FuelFlow.SharedKernel.Errors;
using FuelFlow.SharedKernel.Security.Attestation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using static FuelFlow.API.Extensions.RateLimiterSetup;

namespace FuelFlow.Features.Attestation;

[ApiController]
[Route("api/attestation")]
public sealed class AttestationController : ControllerBase
{
    private readonly AttestationChallengeService _challenges;
    private readonly AttestationService _attestation;

    public AttestationController(
        AttestationChallengeService challenges,
        AttestationService attestation)
    {
        _challenges = challenges;
        _attestation = attestation;
    }

    [HttpPost("challenge")]
    [AllowAnonymous]
    [EnableRateLimiting(DeviceChallengePolicy)]
    [ProducesResponseType(typeof(IssueChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IssueChallenge(
        [FromBody] IssueChallengeRequest request,
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return ApiError.BadRequest(
                "The X-Device-Id header is required.",
                "ATTESTATION_DEVICE_REQUIRED");
        }

        var action = request.Action?.Trim();

        if (action is not (AttestationChallengeService.MobileRequestAction
            or AttestationChallengeService.AppleAttestAction))
        {
            return ApiError.BadRequest(
                "The attestation action is not supported.",
                "ATTESTATION_ACTION_INVALID");
        }

        var requestHash = request.RequestHash?.Trim() ?? string.Empty;

        if (action == AttestationChallengeService.MobileRequestAction
            && !AttestationEncoding.IsBase64UrlSha256(requestHash))
        {
            return ApiError.BadRequest(
                "The request hash must be a base64url SHA-256 digest.",
                "ATTESTATION_REQUEST_HASH_INVALID");
        }

        // Registration is not bound to a business request, so there is no hash to carry.
        if (action == AttestationChallengeService.AppleAttestAction)
            requestHash = string.Empty;

        try
        {
            var issued = await _challenges.IssueAsync(
                deviceId,
                action,
                requestHash,
                cancellationToken);

            return Ok(new IssueChallengeResponse(issued.Challenge, issued.ExpiresAt));
        }
        catch (AttestationVerificationException exception)
        {
            return ApiError.ServiceUnavailable(exception.Message, exception.Code);
        }
    }

    [HttpPost("apple/register")]
    [AllowAnonymous]
    [EnableRateLimiting(DeviceVerifyPolicy)]
    [ProducesResponseType(typeof(RegisterAppleKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RegisterAppleKey(
        [FromBody] RegisterAppleKeyRequest request,
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return ApiError.BadRequest(
                "The X-Device-Id header is required.",
                "ATTESTATION_DEVICE_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(request.KeyId)
            || string.IsNullOrWhiteSpace(request.AttestationObject)
            || string.IsNullOrWhiteSpace(request.Challenge))
        {
            return ApiError.BadRequest(
                "KeyId, AttestationObject and Challenge are required.",
                "ATTESTATION_INVALID");
        }

        if (request.KeyId.Length > 128
            || request.AttestationObject.Length > 64_000
            || request.Challenge.Length > 128)
        {
            return ApiError.BadRequest(
                "The attestation payload is too large.",
                "ATTESTATION_INVALID");
        }

        AppleRegistrationOutcome outcome;

        try
        {
            outcome = await _attestation.RegisterAppleKeyAsync(
                deviceId,
                request.KeyId,
                request.AttestationObject,
                request.Challenge,
                cancellationToken);
        }
        catch (AttestationVerificationException exception)
        {
            return ApiError.ServiceUnavailable(exception.Message, exception.Code);
        }

        if (outcome.Result.IsValid)
        {
            return Ok(new RegisterAppleKeyResponse(true, outcome.AlreadyRegistered));
        }

        if (outcome.Result.Status == AttestationStatus.Unavailable)
        {
            return ApiError.ServiceUnavailable(
                outcome.Result.Detail ?? "Attestation verification is unavailable.",
                outcome.Result.Code);
        }

        if (outcome.Result.Code is "ATTESTATION_KEY_EXISTS" or "ATTESTATION_KEY_REVOKED")
        {
            return ApiError.Conflict(
                outcome.Result.Detail,
                outcome.Result.Code);
        }

        return ApiError.BadRequest(
            outcome.Result.Detail,
            outcome.Result.Code);
    }
}

public sealed record IssueChallengeRequest(string? Action, string? RequestHash);

public sealed record IssueChallengeResponse(string Challenge, DateTimeOffset ExpiresAt);

public sealed record RegisterAppleKeyRequest(
    string? KeyId,
    string? AttestationObject,
    string? Challenge);

public sealed record RegisterAppleKeyResponse(bool Registered, bool AlreadyRegistered);
