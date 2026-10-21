using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Notifications.SharedModels;

public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// What this notification is about, as a routing key: <see cref="NotificationTypes"/>.
    /// </summary>
    /// <remarks>
    /// The copy is not a contract - it is prose, it is translated on the client, and it changes when
    /// the wording changes. This is the contract: it says what the row is FOR, so a client can act
    /// on it without reading the message.
    /// </remarks>
    public string Type { get; set; } = NotificationTypes.Generic;

    /// <summary>
    /// What a client needs to open what the notification is about, as JSON. Null when there is
    /// nothing to open - a plain row carries no target.
    /// </summary>
    public string? Payload { get; set; }

    public User? User { get; set; }
}
