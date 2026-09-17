namespace FuelFlow.Features.Auth.AdminUser.SetUserActive;

/// <summary>
/// Activates or deactivates a user account.
/// </summary>
/// <param name="UserId">The user to toggle.</param>
/// <param name="IsActive">True to activate, false to deactivate.</param>
/// <param name="ActingUserId">The staff member performing the action.</param>
/// <param name="ActingUserName">Display name of the actor (for audit trail).</param>
/// <param name="ActingRole">Role name of the actor (for hierarchy checks).</param>
public sealed record SetUserActiveCommand(
    Guid UserId,
    bool IsActive,
    Guid ActingUserId,
    string? ActingUserName,
    string? ActingRole);

