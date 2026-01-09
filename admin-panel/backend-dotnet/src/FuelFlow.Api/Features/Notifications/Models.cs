namespace FuelFlow.Api.Features.Notifications;

public record Notification
{
    public int Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int Read { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record CreateNotificationRequest
{
    public string UserId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
