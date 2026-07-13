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

    public User? User { get; set; }
}
