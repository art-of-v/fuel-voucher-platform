using System.Security.Claims;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
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

    public NotificationsController(
        GetNotificationsQueryHandler getHandler,
        MarkNotificationReadCommandHandler markReadHandler)
    {
        _getHandler = getHandler;
        _markReadHandler = markReadHandler;
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

    private Guid? GetUserId()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
