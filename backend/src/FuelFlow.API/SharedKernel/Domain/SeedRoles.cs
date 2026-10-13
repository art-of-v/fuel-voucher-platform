namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Canonical role identifiers and helpers.
/// </summary>
/// <remarks>
/// The numeric ranking — ProductOwner (100) &gt; Admin (50) &gt; Manager (10) &gt; QA (5) &gt; User (0) —
/// lives in the <c>roles.level</c> column and is read from the database, not from a switch here.
/// QA sits between Manager and User deliberately: strictly above 0 so promoting a User to QA is
/// not mistaken for a demotion (which would revoke the target's sessions), and strictly below 10
/// so Manager -&gt; QA still counts as one.
/// This class holds only the names the application needs before a database round trip: the
/// authorization policies, and the singleton ProductOwner guard.
/// </remarks>
internal static class SeedRoles
{
    public const string ProductOwnerName = "ProductOwner";
    public const string AdminName = "Admin";
    public const string ManagerName = "Manager";
    public const string QaName = "QA";
    public const string UserName = "User";

    /// <summary>
    /// Every staff role, highest-first. Single source of truth for "who is staff" — used by
    /// IsStaff, the "Staff" authorization policy, and the admin SPA gate. Add a future staff
    /// role here once and it flows everywhere, instead of editing scattered role-name literals.
    /// <para>
    /// Queries that need "is this user staff" answered by the DATABASE must reference this array
    /// rather than spelling the names out again: EF Core cannot translate the <see cref="IsStaff"/>
    /// method call into SQL, which is why earlier versions of those queries kept private copies of
    /// the list — and every copy silently omitted later roles, locking them out of the admin panel.
    /// </para>
    /// </summary>
    public static readonly string[] StaffRoleNames = { ProductOwnerName, AdminName, ManagerName, QaName };

    /// <summary>
    /// Staff roles that may ban/unban/delete and toggle authentication switches (Manager excluded).
    /// Backs the "AdminOrOwner" authorization policy.
    /// </summary>
    public static readonly string[] AdminAndOwnerRoleNames = { ProductOwnerName, AdminName };

    // Fixed, deterministic GUIDs matching the seeded database rows.
    // Admin and User IDs must match existing seeded data (migrations 20260619000001, 20260915121550).
    // ProductOwner, Manager and QA are new roles; use stable random UUIDs.
    public static readonly Guid ProductOwnerId = Guid.Parse("2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c");
    public static readonly Guid AdminRoleId    = Guid.Parse("0b6c503a-2086-4fe3-b617-b47385b474bd");
    public static readonly Guid ManagerRoleId  = Guid.Parse("3d5b9c2f-6a4e-4b8f-ad2c-9e3f7a5b6d8e");
    public static readonly Guid QaRoleId       = Guid.Parse("7d4a1e93-5c62-4b8f-9a03-e6d1c7b48215");
    public static readonly Guid UserRoleId     = Guid.Parse("1b445bd0-6f91-4b5a-ac2d-a9601691142f");

    public static bool IsValidRoleName(string? roleName) =>
        roleName == UserName || IsStaff(roleName);

    public static bool IsStaff(string? roleName) =>
        roleName is not null && Array.IndexOf(StaffRoleNames, roleName) >= 0;
}
