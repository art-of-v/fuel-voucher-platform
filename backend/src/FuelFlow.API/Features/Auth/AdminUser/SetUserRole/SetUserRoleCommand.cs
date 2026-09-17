namespace FuelFlow.Features.Auth.AdminUser.SetUserRole;

/// <summary>
/// Assigns a new role to a user. Enforces hierarchy and last-product-owner protection.
/// </summary>
/// <param name="UserId">The user receiving the new role.</param>
/// <param name="RoleName">Target role name (ProductOwner, Admin, Manager, User).</param>
/// <param name="ActingUserId">The staff member performing the change.</param>
/// <param name="ActingUserName">Display name of the actor (for audit trail).</param>
/// <param name="ActingRole">Role name of the actor (for hierarchy checks).</param>
public sealed record SetUserRoleCommand(
    Guid UserId,
    string RoleName,
    Guid ActingUserId,
    string? ActingUserName,
    string? ActingRole);

