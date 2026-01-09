using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Features.Notifications;

[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository _repository;

    public NotificationsController(INotificationRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        var userId = HttpContext.Session.GetString("userId") ?? "dev-user-123";
        var notifications = await _repository.GetUserNotificationsAsync(userId);
        return Ok(notifications);
    }

    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        await _repository.MarkAsReadAsync(id);
        return Ok(new { success = true });
    }
}
