namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Canonical role identifiers and helpers.
/// </summary>
/// <remarks>
/// The numeric <c>Level</c> encodes the hierarchy enforced by
/// AuthorizationHelpers.CanManage:
///   ProductOwner (100) > Admin (50) > Manager (10) > User (0).
/// </remarks>
internal static class SeedRoles
{
    public const string ProductOwnerName = "ProductOwner";
    public const string AdminName = "Admin";
    public const string ManagerName = "Manager";
    public const string UserName = "User";

    // Fixed, deterministic GUIDs — never regenerating avoids duplicate rows
    // across migrations/environments and keeps FK references stable.
    public static readonly Guid ProductOwnerId = Guid.Parse("ece0f8e0-1234-9bcd-9abc-def0123456a1");
    public static readonly Guid AdminRoleId    = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid ManagerRoleId  = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    public static readonly Guid UserRoleId     = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    public static bool IsValidRoleName(string? roleName) =>
        roleName == UserName || IsStaff(roleName);

    public static bool IsStaff(string? roleName) =>
        roleName == ProductOwnerName || roleName == AdminName || roleName == ManagerName;

    /// <summary>
    /// Numeric hierarchy level for a role name. Unknown roles resolve to 0 (User level).
    /// </summary>
    public static int LevelFor(string? roleName) => roleName switch
    {
        ProductOwnerName => 100,
        AdminName => 50,
        ManagerName => 10,
        _ => 0,
    };
}
