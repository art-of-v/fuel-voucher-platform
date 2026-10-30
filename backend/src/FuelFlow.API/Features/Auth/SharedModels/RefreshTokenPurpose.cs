namespace FuelFlow.Features.Auth.SharedModels;

/// <summary>
/// What a refresh token is for.
/// </summary>
public enum RefreshTokenPurpose
{
    /// <summary>
    /// The session itself: the client keeps this token and rotates it until logout or expiry.
    /// A browser session is one of these with no <c>DeviceId</c> - the cookie is the only copy.
    /// </summary>
    Session = 0,

    /// <summary>
    /// The throwaway a native sign-in receives with its first factor. The client uses the access
    /// token beside it to register and prove its device, then stores only the device-bound token
    /// that the signed challenge mints, so this one is discarded and must not outlive the bind.
    /// </summary>
    DeviceBinding = 1
}

public static class RefreshTokenPurposePolicy
{
    /// <summary>
    /// How long a <see cref="RefreshTokenPurpose.DeviceBinding"/> token may live. It only has to
    /// cover the window between the first factor and the signed challenge - registration nonce
    /// 10 minutes plus challenge 5 - so an abandoned sign-in leaves no long-lived orphan behind.
    /// </summary>
    public static readonly TimeSpan DeviceBindingWindow = TimeSpan.FromMinutes(20);
}
