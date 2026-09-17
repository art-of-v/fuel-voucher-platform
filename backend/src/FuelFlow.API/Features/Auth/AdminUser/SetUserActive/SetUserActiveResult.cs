namespace FuelFlow.Features.Auth.AdminUser.SetUserActive;

public sealed class SetUserActiveResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    public static SetUserActiveResult CreateSuccess() => new() { Success = true };
    public static SetUserActiveResult CreateForbidden() => new() { Forbidden = true };
    public static SetUserActiveResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
}
