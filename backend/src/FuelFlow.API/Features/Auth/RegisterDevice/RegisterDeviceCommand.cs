namespace FuelFlow.Features.Auth.RegisterDevice;

public sealed class RegisterDeviceCommand
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// The signature scheme the client will sign with, named exactly as
    /// <c>AsymmetricSignatureAlgorithm</c> names it. Optional: when absent the server infers it
    /// from the key (RSA -> PKCS#1, EC -> DER) and pins that. A value that contradicts the key's
    /// family is refused.
    /// </summary>
    public string? SignatureAlgorithm { get; set; }

    /// <summary>
    /// The payload shape the client will sign. Optional and defaults to 1, the shape the
    /// installed base uses; a client that understands the domain-separated payload asks for 2.
    /// </summary>
    public int? SignaturePayloadVersion { get; set; }

    public string? DeviceModel { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }

    /// <summary>
    /// One-time proof, issued by /api/auth/verify to the just-authenticated user,
    /// that authorizes rebinding a device_id currently bound to a different user
    /// (phone changed hands, reinstall on a shared device, etc.). Without it a
    /// cross-user collision is refused exactly as before, so a stolen access
    /// token alone still cannot seize another account's device row.
    /// </summary>
    public string? RegistrationNonce { get; set; }
}
