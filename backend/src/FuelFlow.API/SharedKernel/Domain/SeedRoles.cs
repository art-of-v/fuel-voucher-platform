namespace FuelFlow.SharedKernel.Domain;

/// <summary>
/// Canonical fixed ids/names of the built-in roles inserted by the seed migrations
/// (Admin: 20260619000001, User: 20260915121550). The ids are referenced when a new
/// account is registered so mobile customers get the "User" role instead of no role.
/// </summary>
public static class SeedRoles
{
    public const string AdminName = "Admin";
    public const string UserName = "User";

    public static readonly Guid AdminId = new("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid UserRoleId = new("a0000000-0000-0000-0000-000000000002");
}
