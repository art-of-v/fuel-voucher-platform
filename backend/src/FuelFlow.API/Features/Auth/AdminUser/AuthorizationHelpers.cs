using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.AdminUser;

/// <summary>
/// Server-side hierarchy enforcement for admin user-management actions.
/// </summary>
/// <remarks>
/// Hierarchy levels:
///   ProductOwner (100) > Admin (50) > Manager (10) > User (0)
///
/// Rules:
/// - ProductOwner can manage anyone (activate/deactivate/set-role for all roles).
/// - Admin can manage Admin/Manager/User, but can never touch ProductOwner.
/// - Manager can manage User only; can activate/deactivate User but cannot set roles.
/// - User role cannot manage anyone.
/// </remarks>
public static class AuthorizationHelpers
{
    /// <summary>
    /// Returns true if an actor with <paramref name="actorRole"/> may perform <paramref name="action"/>
    /// on a target with <paramref name="targetRoleName"/>.
    /// </summary>
    public static bool CanManage(string? actorRole, string? targetRoleName, string action)
    {
        var actorLevel = SeedRoles.LevelFor(actorRole);

        // Nobody can be managed by a lower-tier role.
        if (actorLevel == 0)
            return false;

        // ProductOwner is absolute — can do anything to anyone.
        if (actorLevel == 100)
            return true;

        // Admin (50): can manage Admin/Manager/User, never ProductOwner.
        if (actorLevel == 50)
        {
            // If target is ProductOwner, nobody except ProductOwner can touch it.
            if (SeedRoles.LevelFor(targetRoleName) == 100)
                return false;

            if (action == "activate" || action == "deactivate")
                return true; // can manage Admin/Manager/User

            if (action == "set-role")
                // Admin can set roles to Admin/Manager/User (any tier <= 50), cannot elevate to ProductOwner.
                return SeedRoles.LevelFor(targetRoleName) <= 50 && SeedRoles.LevelFor(targetRoleName) >= 0;

            return false;
        }

        // Manager (10): manage User only.
        if (actorLevel == 10)
        {
            if (action == "activate" || action == "deactivate")
                return targetRoleName == "User";
            return false; // cannot set roles at all
        }

        return false;
    }

    public static bool RoleEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool IsBelow(string? actorRole, string? targetRole) =>
        SeedRoles.LevelFor(actorRole) > SeedRoles.LevelFor(targetRole);
}
