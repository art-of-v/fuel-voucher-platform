namespace FuelFlow.Features.Auth.AdminUser.SetUserEmail;

/// <summary>
/// An admin proposes a new email for a user. A non-empty <paramref name="NewEmail"/> starts a
/// verified change: the address is held pending until the recipient confirms via the emailed link.
/// An empty/whitespace <paramref name="NewEmail"/> clears the address directly (no verification -
/// staff simply fall back to SMS for OTP). <paramref name="ConfirmBaseUrl"/> is the public origin
/// the confirmation link is built from (e.g. https://api.palne.shop), taken from the request.
/// </summary>
public sealed record SetUserEmailCommand(
    Guid UserId,
    string? NewEmail,
    Guid ActingUserId,
    string? ActingName,
    string? ActingRole,
    string ConfirmBaseUrl);
