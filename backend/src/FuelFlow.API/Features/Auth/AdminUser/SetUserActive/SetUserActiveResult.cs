namespace FuelFlow.Features.Auth.AdminUser.SetUserActive;

public sealed class SetUserActiveResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    public static SetUserActiveResult CreateSuccess() => new() { Success = true };

    /// <summary>
    /// Refused, with the reason. The reason is not decoration: it is what the operator sees, and
    /// an empty 403 left them with a toast that said nothing (planning #299).
    /// </summary>
    public static SetUserActiveResult CreateForbidden(string reason) => new() { Forbidden = true, Error = reason };

    public static SetUserActiveResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
}
