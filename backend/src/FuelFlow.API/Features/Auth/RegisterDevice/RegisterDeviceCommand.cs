namespace FuelFlow.Features.Auth.RegisterDevice;

public sealed class RegisterDeviceCommand
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
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
