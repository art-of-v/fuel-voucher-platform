namespace FuelFlow.Features.Auth.AdminUser.SetUserRole;

public sealed class SetUserRoleResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    public static SetUserRoleResult CreateSuccess() => new() { Success = true };
    public static SetUserRoleResult CreateForbidden() => new() { Forbidden = true };
    public static SetUserRoleResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
    public static SetUserRoleResult Failure(string error) => new() { Error = error };
}
