using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Security;

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

    /// <summary>
    /// The one signature scheme this device is allowed to use. Null only for rows enrolled before
    /// the pin existed; the first successful verification records the scheme that worked.
    /// </summary>
    public AsymmetricSignatureAlgorithm? SignatureAlgorithm { get; set; }

    /// <summary>
    /// The shape of the bytes this device signs (1 = legacy raw payload, 2 = domain-separated).
    /// Pinned per device so a client cannot silently downgrade, and so a new client can opt into
    /// version 2 while the installed base stays on version 1.
    /// </summary>
    public int SignaturePayloadVersion { get; set; } = 1;

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
