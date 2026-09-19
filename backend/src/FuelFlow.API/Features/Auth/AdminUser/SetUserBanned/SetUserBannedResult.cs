namespace FuelFlow.Features.Auth.AdminUser.SetUserBanned;

public sealed class SetUserBannedResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    public static SetUserBannedResult CreateSuccess() => new() { Success = true };
    public static SetUserBannedResult CreateForbidden() => new() { Forbidden = true };
    public static SetUserBannedResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
    public static SetUserBannedResult Failure(string error) => new() { Error = error };
}
