namespace FuelFlow.Features.Auth.AdminUser.SetPromoAccess;

/// <summary>
/// Grants or revokes one customer's access to the promotion.
/// </summary>
/// <param name="UserId">The customer account the access belongs to.</param>
/// <param name="Granted">True to give it, false to take it away.</param>
/// <param name="ActingUserId">The staff member performing the action.</param>
/// <param name="ActingUserName">Display name of the actor (for audit trail).</param>
/// <param name="ActingRole">Role name of the actor (for the refusal that names who may do this).</param>
public sealed record SetPromoAccessCommand(
    Guid UserId,
    bool Granted,
    Guid ActingUserId,
    string? ActingUserName,
    string? ActingRole);
