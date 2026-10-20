namespace FuelFlow.Features.Auth.AdminUser.SetUserRole;

public sealed class SetUserRoleResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    public static SetUserRoleResult CreateSuccess() => new() { Success = true };

    /// <summary>Refused, with the reason - an empty 403 told the operator nothing (planning #299).</summary>
    public static SetUserRoleResult CreateForbidden(string reason) => new() { Forbidden = true, Error = reason };
    public static SetUserRoleResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
    public static SetUserRoleResult Failure(string error) => new() { Error = error };
}
