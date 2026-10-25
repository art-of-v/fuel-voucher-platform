using System.Security.Claims;
using FuelFlow.Features.Notifications.ParkPushToken;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Notifications.RegisterPushToken;
using FuelFlow.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Features.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly GetNotificationsQueryHandler _getHandler;
    private readonly MarkNotificationReadCommandHandler _markReadHandler;
    private readonly RegisterPushTokenCommandHandler _registerPushTokenHandler;
    private readonly ParkPushTokenCommandHandler _parkPushTokenHandler;

    public NotificationsController(
        GetNotificationsQueryHandler getHandler,
        MarkNotificationReadCommandHandler markReadHandler,
        RegisterPushTokenCommandHandler registerPushTokenHandler,
        ParkPushTokenCommandHandler parkPushTokenHandler)
    {
        _getHandler = getHandler;
        _markReadHandler = markReadHandler;
        _registerPushTokenHandler = registerPushTokenHandler;
        _parkPushTokenHandler = parkPushTokenHandler;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return ApiError.Unauthorized("Unauthorized");

        var result = await _getHandler.HandleAsync(new GetNotificationsQuery(userId.Value), cancellationToken);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(typeof(MarkNotificationReadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return ApiError.Unauthorized("Unauthorized");

        var result = await _markReadHandler.HandleAsync(new MarkNotificationReadCommand(id, userId.Value), cancellationToken);
        if (result == null) return ApiError.NotFound("Not found");

        return Ok(result);
    }

    [HttpPost("push-tokens")]
    [ProducesResponseType(typeof(RegisterPushTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegisterPushToken(
        [FromBody] RegisterPushTokenRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return ApiError.Unauthorized("Unauthorized");

        var token = request?.Token?.Trim();
        if (string.IsNullOrEmpty(token) || !IsExpoPushToken(token))
            return ApiError.BadRequest("Invalid Expo push token.");

        var platform = string.IsNullOrWhiteSpace(request!.Platform)
            ? "unknown"
            : request.Platform!.Trim().ToLowerInvariant();

        // Prefer an explicit body deviceId; otherwise reuse the x-device-id the client
        // already sends on every request (see mobile apiClient), for later dedupe.
        var deviceId = request.DeviceId?.Trim();
        if (string.IsNullOrEmpty(deviceId))
            deviceId = Request.Headers["x-device-id"].FirstOrDefault();

        var result = await _registerPushTokenHandler.HandleAsync(
            new RegisterPushTokenCommand(userId.Value, token, platform, deviceId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Parks this device's push token: the device stops receiving the platform's business
    /// notifications, but it stays addressable for the sign-in code.
    /// </summary>
    [HttpPost("push-tokens/park")]
    [ProducesResponseType(typeof(ParkPushTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ParkPushToken(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
        => await ParkAsync(token, cancellationToken);

    /// <summary>
    /// The same parking, on the route the shipped fleet build (236) calls when it logs out.
    /// </summary>
    /// <remarks>
    /// That build cannot be moved to the POST route without a new OTA, and a token this route
    /// deactivated meant the next sign-in code fell back to the paid SMS channel - the one the
    /// push channel exists to avoid. So the route parks, exactly like POST push-tokens/park:
    /// it no longer removes anything.
    /// </remarks>
    [HttpDelete("push-tokens")]
    [ProducesResponseType(typeof(ParkPushTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeregisterPushToken(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
        => await ParkAsync(token, cancellationToken);

    private async Task<IActionResult> ParkAsync(string? token, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return ApiError.Unauthorized("Unauthorized");

        var trimmedToken = token?.Trim();

        // On logout the client can't cheaply re-mint its Expo token, so the x-device-id it already
        // sends on every request (see mobile apiClient) is the primary selector; an explicit
        // ?token= is honoured when a caller has it. The handler no-ops if neither is present.
        var deviceId = Request.Headers["x-device-id"].FirstOrDefault()?.Trim();

        var result = await _parkPushTokenHandler.HandleAsync(
            new ParkPushTokenCommand(
                userId.Value,
                string.IsNullOrEmpty(trimmedToken) ? null : trimmedToken,
                string.IsNullOrEmpty(deviceId) ? null : deviceId),
            cancellationToken);

        return Ok(result);
    }

    private static bool IsExpoPushToken(string token) =>
        (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
         || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith("]", StringComparison.Ordinal);

    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
