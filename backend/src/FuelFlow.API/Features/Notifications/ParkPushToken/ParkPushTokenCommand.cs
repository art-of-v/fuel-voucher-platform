namespace FuelFlow.Features.Notifications.ParkPushToken;

/// <summary>
/// Parks the caller's push token(s) on logout: the device stops receiving the platform's
/// business notifications, but it stays addressable for the sign-in code.
/// </summary>
/// <remarks>
/// <para>
/// Parking is not deactivating. A deactivated token (<c>is_active = false</c>) means Expo
/// reported the device is gone - a replaced phone - and nothing may be sent to it. A parked
/// token is a device that signed OUT, and that is exactly where a worker signs back in from:
/// deactivating it on logout sent every sign-in code back onto the paid SMS channel, which
/// is the channel the push channel exists to avoid.
/// </para>
/// <para>
/// The sign-in code is addressed to the account's devices, never to the device that asked for
/// it, so a code cannot be handed to whoever happens to hold the phone.
/// </para>
/// </remarks>
public sealed record ParkPushTokenCommand(Guid UserId, string? Token, string? DeviceId);
