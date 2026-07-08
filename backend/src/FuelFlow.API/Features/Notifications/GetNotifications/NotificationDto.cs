namespace FuelFlow.Features.Notifications.GetNotifications;

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAt);
