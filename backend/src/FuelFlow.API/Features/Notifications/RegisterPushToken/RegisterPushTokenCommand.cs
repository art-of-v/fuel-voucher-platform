namespace FuelFlow.Features.Notifications.RegisterPushToken;

public sealed record RegisterPushTokenCommand(Guid UserId, string Token, string Platform, string? DeviceId);
