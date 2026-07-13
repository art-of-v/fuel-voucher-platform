namespace FuelFlow.Features.Notifications.MarkNotificationRead;

public sealed record MarkNotificationReadCommand(Guid NotificationId, Guid UserId);
