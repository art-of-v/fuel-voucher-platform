using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.AdminUser;

/// <summary>
/// Server-side guard for the staff hierarchy:
/// ProductOwner(100) &gt; Admin(50) &gt; Manager(10) &gt; User(0).
/// ProductOwner can do anything (absolute rights, incl. creating Admins).
/// Admins are product developers under the owner: they manage User and Manager
/// accounts only (Admins themselves are created by the ProductOwner only).
/// Managers are employees: they activate/deactivate User accounts only and
/// never change roles. Users have no staff rights at all.
/// The admin SPA mirrors these rules cosmetically, but this class is the
/// enforcement point — never trust the client dropdown.
/// </summary>
public static class RoleHierarchy
{
    public static bool CanActivateDeactivate(string? actorRole, string? targetRole)
    {
        if (!SeedRoles.IsStaff(actorRole) || targetRole is null)
            return false;

        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return targetRole == SeedRoles.UserName || targetRole == SeedRoles.ManagerName;

        if (actorRole == SeedRoles.ManagerName)
            return targetRole == SeedRoles.UserName;

        return false;
    }

    public static bool CanAssignRole(string? actorRole, string? targetCurrentRole, string? newRole)
    {
        if (!SeedRoles.IsValidRoleName(newRole) || targetCurrentRole is null)
            return false;

        // ProductOwner is a singleton established ONLY via the env bootstrap
        // (Auth:BootstrapProductOwnerPhone). It is never assignable through the app, so no
        // actor - not even a ProductOwner - can mint a second one...
        if (newRole == SeedRoles.ProductOwnerName)
            return false;

        // ...and nobody can change an existing ProductOwner's role: the PO is untouchable.
        if (targetCurrentRole == SeedRoles.ProductOwnerName)
            return false;

        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return (targetCurrentRole == SeedRoles.UserName || targetCurrentRole == SeedRoles.ManagerName)
                && (newRole == SeedRoles.UserName || newRole == SeedRoles.ManagerName);

        return false;
    }

    public static bool CanDelete(string? actorRole, string? targetRole)
    {
        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return targetRole == SeedRoles.UserName || targetRole == SeedRoles.ManagerName;

        return false;
    }

    /// <summary>
    /// Who may ban/unban whom. Ban is a hard revocation (kills every session), so it carries
    /// the same authority as delete, NOT the looser activate/deactivate rule: a Manager can
    /// deactivate a User's purchasing but cannot ban anyone. ProductOwner may ban anyone
    /// (incl. another Admin/ProductOwner); Admin may ban only User and Manager accounts.
    /// </summary>
    public static bool CanBan(string? actorRole, string? targetRole)
    {
        if (targetRole is null)
            return false;

        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return targetRole == SeedRoles.UserName || targetRole == SeedRoles.ManagerName;

        return false;
    }
}