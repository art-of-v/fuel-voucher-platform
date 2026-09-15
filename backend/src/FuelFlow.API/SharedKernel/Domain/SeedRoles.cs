namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Canonical fixed ids/names of the built-in roles inserted by the seed migrations
/// (Admin: 20260619000001, User: 20260915121550, re-keyed to proper UUIDs by
/// 20260915143007_NormalizeSeedRoleIds). The ids are referenced when a new
/// account is registered so mobile customers get the "User" role instead of no role.
/// </summary>
public static class SeedRoles
{
    public const string AdminName = "Admin";
    public const string UserName = "User";

    public static readonly Guid AdminId = new("0b6c503a-2086-4fe3-b617-b47385b474bd");
    public static readonly Guid UserRoleId = new("1b445bd0-6f91-4b5a-ac2d-a9601691142f");
}
