namespace FuelFlow.Features.Auth.RegisterDevice;

public sealed class RegisterDeviceCommand
{
    public Guid UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? DeviceModel { get; set; }
    public string? OsVersion { get; set; }
    public string? AppVersion { get; set; }
}
