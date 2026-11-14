using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Auth.AdminUser;

/// <summary>
/// Server-side guard for the staff hierarchy:
/// ProductOwner(100) &gt; Admin(50) &gt; Manager(10) &gt; User(0).
/// ProductOwner can do anything (absolute rights, incl. creating Admins).
/// Admins are product developers under the owner: they manage User and Manager
/// accounts only (Admins themselves are created by the ProductOwner only).
/// Managers are employees: they activate/deactivate User accounts, and they move
/// money — a refund, a payment placed for a customer, an order's status. Users
/// have no staff rights at all.
/// <para>
/// <b>The retired `QA` staff role.</b> It used to sit between Manager and User and admitted a
/// tester to every endpoint the Staff policy covers, including the ones that move money. The
/// owner ruled on 2026-10-21 that the QA identity the product needs is the client-side test
/// account — an ordinary <c>User</c> row with <c>is_qa_account = true</c>, a sandboxed merchant
/// and the QA stock pool — and that the staff role is unused. So the role is no longer staff:
/// <c>SeedRoles.IsStaff</c> answers "no", <c>CanAssignRole</c> refuses to hand it out, and every
/// rule below refuses it as an actor. The <c>roles</c> row still exists; a name the code no
/// longer knows would make a row the database may still carry unnameable.
/// </para>
/// <para>
/// <b>Money movement and the order.</b> The hierarchy says who may move money. It does not say
/// that money has been received: an order may only be handed fuel once a payment for it has been
/// captured, and that rule lives with the order, not with the role — so it holds for every actor,
/// the ProductOwner included.
/// </para>
/// <para>
/// <b>An account with no role at all.</b> `users.role_id` is nullable and the backfill did not
/// cover every row, so a roleless account is a reachable state: the hierarchy has no place to put
/// it, and every rule below used to answer "no" for it - including for the ProductOwner, whose
/// absolute rights that contradicted. The account was then unreachable through the whole admin
/// surface (activate, deactivate, re-role, email, ban): a dead end only a psql session could leave.
/// The ProductOwner is now the one actor who can manage such an account, which is both the
/// recovery path and the reason the refusals carry a sentence saying who to ask for it.
/// </para>
/// The admin SPA mirrors these rules cosmetically, but this class is the
/// enforcement point — never trust the client dropdown.
/// </summary>
public static class RoleHierarchy
{
    public static bool CanActivateDeactivate(string? actorRole, string? targetRole)
    {
        if (!SeedRoles.IsStaff(actorRole))
            return false;

        // A target with no role sits outside every tier: the hierarchy cannot place it, so nobody
        // below the ProductOwner can be said to manage it. The ProductOwner can - and has to, or
        // the account stays stuck for good (planning #299).
        if (targetRole is null)
            return actorRole == SeedRoles.ProductOwnerName;

        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return targetRole == SeedRoles.UserName || targetRole == SeedRoles.ManagerName;

        // Manager is the employee tier now: User accounts only. It may not flip the switch on a
        // colleague's own account — that is the Admin's privilege.
        if (actorRole == SeedRoles.ManagerName)
            return targetRole == SeedRoles.UserName;

        return false;
    }

    public static bool CanAssignRole(string? actorRole, string? targetCurrentRole, string? newRole)
    {
        if (!SeedRoles.IsValidRoleName(newRole))
            return false;

        // ProductOwner is a singleton established ONLY via the env bootstrap
        // (Auth:BootstrapProductOwnerPhone). It is never assignable through the app, so no
        // actor - not even a ProductOwner - can mint a second one...
        if (newRole == SeedRoles.ProductOwnerName)
            return false;

        // ...and nobody can change an existing ProductOwner's role: the PO is untouchable.
        if (targetCurrentRole == SeedRoles.ProductOwnerName)
            return false;

        if (!SeedRoles.IsStaff(actorRole))
            return false;

        // A target with no role is the ProductOwner's to place: this is the only way such an
        // account ever gets a role at all, so the owner is the only actor who may do it
        // (planning #299).
        if (targetCurrentRole is null)
            return actorRole == SeedRoles.ProductOwnerName;

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
        if (!SeedRoles.IsStaff(actorRole))
            return false;

        // A target with no role is the ProductOwner's to ban, for the same reason it is the
        // ProductOwner's to activate and to re-role (planning #299).
        if (targetRole is null)
            return actorRole == SeedRoles.ProductOwnerName;

        if (actorRole == SeedRoles.ProductOwnerName)
            return true;

        if (actorRole == SeedRoles.AdminName)
            return targetRole == SeedRoles.UserName || targetRole == SeedRoles.ManagerName;

        return false;
    }

    /// <summary>
    /// Why an actor cannot manage a target, in one sentence. The refusal used to be an empty 403:
    /// the operator saw "failed to change the user's status" with no reason attached, and the
    /// reason was written only to the server log (planning #299).
    /// </summary>
    /// <param name="actorRole">The acting staff role, or null when the caller carries none.</param>
    /// <param name="targetRole">The target's role, or null when the account has none.</param>
    public static string DescribeRefusal(string? actorRole, string? targetRole)
    {
        if (targetRole is null)
            return "This account has no role, so only the ProductOwner can manage it.";

        if (!SeedRoles.IsStaff(actorRole))
            return "Only staff can manage an account.";

        return $"A {actorRole} cannot manage a {targetRole} account.";
    }
}