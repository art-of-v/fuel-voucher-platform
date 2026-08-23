namespace FuelFlow.API.Features.Auth.RegisterDevice
{
    public sealed class RegisterDeviceResponse
    {
        public Guid DeviceIdGuid { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }

        /// <summary>
        /// Non-null when registration was refused. The only current value is
        /// "DeviceAlreadyRegistered", returned when the supplied device_id is already
        /// enrolled to a different account; the controller maps it to 409 Conflict.
        /// </summary>
        public string? Error { get; set; }
    }
}
