namespace FuelFlow.Features.Auth.AdminUser;

/// <summary>
/// Server-side helpers for admin user-management actions.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy <b>rules</b> live in <see cref="RoleHierarchy"/>, which compares role names and is
/// the enforcement point for every admin action. The hierarchy <b>ranking</b> — which role outranks
/// which — lives in <c>roles.level</c>, read from the database.
/// </para>
/// <para>
/// This class once also carried <c>CanManage</c> and <c>IsBelow</c>, which ranked roles through a
/// hardcoded C# switch (<c>SeedRoles.LevelFor</c>). Both were removed because they had no callers,
/// and because they did not agree with the live rules: <c>CanManage</c> let an Admin deactivate
/// <i>any</i> account below ProductOwner, while <see cref="RoleHierarchy.CanActivateDeactivate"/>
/// lets an Admin touch only User and Manager. Two implementations of an authorization rule that
/// disagree is a hazard even when one of them is unreachable — the next person wires the readable
/// one up and silently widens an Admin's reach.
/// </para>
/// </remarks>
public static class AuthorizationHelpers
{
    public static bool RoleEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}