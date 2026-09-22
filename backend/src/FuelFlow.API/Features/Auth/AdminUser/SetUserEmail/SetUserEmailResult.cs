namespace FuelFlow.Features.Auth.AdminUser.SetUserEmail;

public sealed class SetUserEmailResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    /// <summary>True when a verification email was sent and the change is awaiting confirmation
    /// (as opposed to an immediate clear).</summary>
    public bool PendingConfirmation { get; init; }

    public static SetUserEmailResult CreatePending() => new() { Success = true, PendingConfirmation = true };
    public static SetUserEmailResult CreateCleared() => new() { Success = true };
    public static SetUserEmailResult CreateForbidden() => new() { Forbidden = true };
    public static SetUserEmailResult CreateNotFound(string error) => new() { NotFound = true, Error = error };
    public static SetUserEmailResult Failure(string error) => new() { Error = error };
}
