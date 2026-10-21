namespace FuelFlow.Features.Notifications.GetNotifications;

/// <summary>
/// One notification, as the client sees it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Type</c> says what the row is about and <c>LegalEntityId</c> / <c>OrderId</c> say what it
/// opens. They come from the row's <c>payload</c>, flattened onto the wire: the client should not
/// have to know that the payload is JSON, and the two ids are the only two things a notification
/// can point at today.
/// </para>
/// <para>
/// Both ids are nullable because the target may be gone by the time the notification is read - a
/// company can be deleted, an order can be deleted. The client checks the target still resolves
/// before it navigates.
/// </para>
/// </remarks>
public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    bool IsRead,
    DateTime CreatedAt,
    string Type,
    Guid? LegalEntityId,
    Guid? OrderId);
