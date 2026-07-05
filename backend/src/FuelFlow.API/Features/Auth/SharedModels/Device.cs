using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.SharedModels;

public sealed class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    /// <summary>
    /// The corresponding private key never leaves the device.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    public string? DeviceModel { get; set; }

    public string? OsVersion { get; set; }

    public string? AppVersion { get; set; }

    /// <summary>
    /// Revoked devices cannot authenticate even with valid signatures.
    /// </summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public User? User { get; set; }
}

public enum DeviceStatus
{
    Active,
    Revoked
}
