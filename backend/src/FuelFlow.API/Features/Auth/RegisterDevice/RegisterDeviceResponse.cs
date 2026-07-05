namespace FuelFlow.API.Features.Auth.RegisterDevice
{
    public sealed class RegisterDeviceResponse
    {
        public Guid DeviceIdGuid { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
    }
}
