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

    // Fixed, deterministic GUIDs matching the seeded database rows.
    // Admin and User IDs must match existing seeded data (migrations 20260619000001, 20260915121550).
    // ProductOwner and Manager are new roles; use stable random UUIDs.
    public static readonly Guid ProductOwnerId = Guid.Parse("2c4a8b1e-5f3d-4a7e-9c1b-8d2e6f4a3b5c");
    public static readonly Guid AdminRoleId    = Guid.Parse("0b6c503a-2086-4fe3-b617-b47385b474bd");
    public static readonly Guid ManagerRoleId  = Guid.Parse("3d5b9c2f-6a4e-4b8f-ad2c-9e3f7a5b6d8e");
    public static readonly Guid UserRoleId     = Guid.Parse("1b445bd0-6f91-4b5a-ac2d-a9601691142f");

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
