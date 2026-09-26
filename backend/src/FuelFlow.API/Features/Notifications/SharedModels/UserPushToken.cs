using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Notifications.SharedModels;

public sealed class UserPushToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>The Expo push token (ExponentPushToken[...]). Unique across users.</summary>
    public string Token { get; set; } = null!;

    /// <summary>Reporting platform of the registering client: "ios" / "android".</summary>
    public string Platform { get; set; } = null!;

    /// <summary>Optional correlation to the registering device (devices.device_id).</summary>
    public string? DeviceId { get; set; }

    /// <summary>Cleared when Expo later reports the token is no longer registered.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    public User? User { get; set; }
}
