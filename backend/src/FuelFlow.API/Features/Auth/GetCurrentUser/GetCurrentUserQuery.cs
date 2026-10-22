namespace FuelFlow.Features.Auth.GetCurrentUser;

/// <summary>
/// The caller's own account, read by id.
/// </summary>
/// <param name="UserId">The id from the caller's <c>NameIdentifier</c> claim.</param>
public sealed record GetCurrentUserQuery(Guid UserId);
