using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.VerifyChallenge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
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

    public DeviceAuthController(
        RegisterDeviceCommandHandler registerDeviceHandler,
        GenerateChallengeCommandHandler generateChallengeHandler,
        VerifyChallengeCommandHandler verifyChallengeHandler,
        LogoutDeviceCommandHandler logoutDeviceHandler)
    {
        _registerDeviceHandler = registerDeviceHandler;
        _generateChallengeHandler = generateChallengeHandler;
        _verifyChallengeHandler = verifyChallengeHandler;
        _logoutDeviceHandler = logoutDeviceHandler;
    }

    [HttpPost("register")]
    [Authorize]
    [ProducesResponseType(typeof(RegisterDeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return BadRequest("DeviceId is required");

        if (string.IsNullOrWhiteSpace(command.PublicKey))
            return BadRequest("PublicKey is required");

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized("Invalid user token");

        command.UserId = userId;

        var result = await _registerDeviceHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("challenge")]
    [EnableRateLimiting(DeviceChallengePolicy)]
    [ProducesResponseType(typeof(GenerateChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateChallenge(
        [FromBody] GenerateChallengeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return BadRequest("DeviceId is required");

        var result = await _generateChallengeHandler.HandleAsync(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("verify")]
    [EnableRateLimiting(DeviceVerifyPolicy)]
    [ProducesResponseType(typeof(VerifyChallengeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyChallenge(
        [FromBody] VerifyChallengeCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DeviceId))
            return BadRequest("DeviceId is required");

        if (string.IsNullOrWhiteSpace(command.Challenge))
            return BadRequest("Challenge is required");

        if (string.IsNullOrWhiteSpace(command.Signature))
            return BadRequest("Signature is required");

        var result = await _verifyChallengeHandler.HandleAsync(command, cancellationToken);

        if (!result.IsValid)
            return Unauthorized(result.Error);

        return Ok(result);
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
}
