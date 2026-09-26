namespace FuelFlow.Features.Notifications.DeregisterPushToken;

/// <summary>
/// Deactivates the caller's push token(s) on logout so the server stops targeting a device the
/// user has signed out of. Matches by <see cref="Token"/> and/or <see cref="DeviceId"/>; at least
/// one must be non-null or the command is a no-op. Only the caller's own rows are ever touched.
/// </summary>
public sealed record DeregisterPushTokenCommand(Guid UserId, string? Token, string? DeviceId);
