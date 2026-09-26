using System.Security.Claims;
using FuelFlow.Features.Notifications.DeregisterPushToken;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Notifications.RegisterPushToken;
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
    private readonly DeregisterPushTokenCommandHandler _deregisterPushTokenHandler;

    public NotificationsController(
        GetNotificationsQueryHandler getHandler,
        MarkNotificationReadCommandHandler markReadHandler,
        RegisterPushTokenCommandHandler registerPushTokenHandler,
        DeregisterPushTokenCommandHandler deregisterPushTokenHandler)
    {
        _getHandler = getHandler;
        _markReadHandler = markReadHandler;
        _registerPushTokenHandler = registerPushTokenHandler;
        _deregisterPushTokenHandler = deregisterPushTokenHandler;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

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
        if (userId == null) return Unauthorized();

        var result = await _markReadHandler.HandleAsync(new MarkNotificationReadCommand(id, userId.Value), cancellationToken);
        if (result == null) return NotFound();

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
        if (userId == null) return Unauthorized();

        var token = request?.Token?.Trim();
        if (string.IsNullOrEmpty(token) || !IsExpoPushToken(token))
            return BadRequest(new { error = new { message = "Invalid Expo push token." } });

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

    [HttpDelete("push-tokens")]
    [ProducesResponseType(typeof(DeregisterPushTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeregisterPushToken(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var trimmedToken = token?.Trim();

        // On logout the client can't cheaply re-mint its Expo token, so the x-device-id it already
        // sends on every request (see mobile apiClient) is the primary selector; an explicit
        // ?token= is honoured when a caller has it. The handler no-ops if neither is present.
        var deviceId = Request.Headers["x-device-id"].FirstOrDefault()?.Trim();

        var result = await _deregisterPushTokenHandler.HandleAsync(
            new DeregisterPushTokenCommand(
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
