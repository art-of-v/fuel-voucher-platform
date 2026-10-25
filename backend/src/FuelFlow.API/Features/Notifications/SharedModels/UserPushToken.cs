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

    /// <summary>
    /// True when the device has signed out: it no longer receives the platform's business
    /// notifications, but it stays addressable for the sign-in code.
    /// </summary>
    /// <remarks>
    /// A signed-out device is exactly where a worker signs back in from, and a sign-in code
    /// that cannot reach a signed-out device has to be paid for by SMS - the channel the push
    /// channel exists to avoid. The token itself stays valid (Expo still knows it); only the
    /// audience narrowed. A successful sign-in clears the flag.
    /// </remarks>
    public bool AuthOtpOnly { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    public User? User { get; set; }
}
