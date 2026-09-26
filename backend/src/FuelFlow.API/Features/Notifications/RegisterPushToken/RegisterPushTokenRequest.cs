namespace FuelFlow.Features.Notifications.RegisterPushToken;

public sealed class RegisterPushTokenRequest
{
    public string? Token { get; init; }
    public string? Platform { get; init; }
    public string? DeviceId { get; init; }
}
