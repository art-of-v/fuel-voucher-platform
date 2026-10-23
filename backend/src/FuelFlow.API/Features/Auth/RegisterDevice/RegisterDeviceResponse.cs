namespace FuelFlow.API.Features.Auth.RegisterDevice
{
    public sealed class RegisterDeviceResponse
    {
        public Guid DeviceIdGuid { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }

        /// <summary>
        /// Non-null when registration was refused.
        /// <para>
        /// "DeviceAlreadyRegistered" means the supplied device_id is already enrolled to a
        /// different account, and the controller maps it to 409 Conflict. The enrollment-contract
        /// failures - "InvalidPublicKey", "UnsupportedSignatureAlgorithm",
        /// "SignatureAlgorithmDoesNotMatchKey" and "UnsupportedSignaturePayloadVersion" - are
        /// caller mistakes and map to 400 Bad Request.
        /// </para>
        /// </summary>
        public string? Error { get; set; }
    }
}
