namespace FuelFlow.Features.Auth.AdminUser.SetPromoAccess;

/// <summary>
/// What the panel is told about one customer's promotion access — including the state it found, so a
/// refusal and an idempotent repeat are not confused with a change.
/// </summary>
public sealed class SetPromoAccessResult
{
    public bool Success { get; init; }
    public bool Forbidden { get; init; }
    public bool NotFound { get; init; }
    public string? Error { get; init; }

    /// <summary>Whether the customer has the access after this call, whatever the call did to get there.</summary>
    public bool Granted { get; init; }

    /// <summary>When it was given, or NULL when the customer does not have it.</summary>
    public DateTime? GrantedAtUtc { get; init; }

    public static SetPromoAccessResult Create(bool granted, DateTime? grantedAtUtc) => new()
    {
        Success = true,
        Granted = granted,
        GrantedAtUtc = grantedAtUtc,
    };

    /// <summary>
    /// Refused, with the sentence the operator sees. An empty 403 left them guessing what had been
    /// refused — the same finding activate/deactivate carries (planning #299).
    /// </summary>
    public static SetPromoAccessResult CreateForbidden(string reason) => new() { Forbidden = true, Error = reason };

    public static SetPromoAccessResult CreateNotFound(string reason) => new() { NotFound = true, Error = reason };
}
