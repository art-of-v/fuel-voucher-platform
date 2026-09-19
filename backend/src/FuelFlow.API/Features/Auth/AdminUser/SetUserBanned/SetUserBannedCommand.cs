namespace FuelFlow.Features.Auth.AdminUser.SetUserBanned;

/// <summary>
/// Bans or unbans a user account.
///
/// <para>
/// A ban is a hard revocation, not just a flag: it bumps <c>TokenVersion</c> and revokes
/// every active refresh token and device, so all live sessions (mobile and admin) die within
/// one request via <see cref="Middleware.SessionValidationMiddleware"/>. Because the ban also
/// invalidates the refresh tokens, an <b>unban</b> deliberately does NOT restore prior sessions
/// — the user must authenticate from scratch. This asymmetry lives in the handler.
/// </para>
/// </summary>
/// <param name="UserId">The user to ban or unban.</param>
/// <param name="IsBanned">True to ban (revoke all sessions), false to unban (no session restore).</param>
/// <param name="ActingUserId">The staff member performing the action.</param>
/// <param name="ActingUserName">Display name of the actor (for audit trail).</param>
/// <param name="ActingRole">Role name of the actor (for hierarchy checks).</param>
public sealed record SetUserBannedCommand(
    Guid UserId,
    bool IsBanned,
    Guid ActingUserId,
    string? ActingUserName,
    string? ActingRole);
