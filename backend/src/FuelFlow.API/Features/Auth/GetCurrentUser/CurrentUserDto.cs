namespace FuelFlow.Features.Auth.GetCurrentUser;

/// <summary>
/// The account the caller is logged in as.
/// </summary>
/// <remarks>
/// The property names ARE the API contract: the mobile app and the admin SPA read
/// <c>id</c>, <c>phone</c>, <c>firstName</c> and the rest off this body, and the names are
/// lower camel case because the API serializes with the web defaults. Renaming one of them
/// silently empties a field on somebody's screen.
/// </remarks>
public sealed record CurrentUserDto(
    Guid Id,
    string Phone,
    string? Email,
    string? FirstName,
    string? LastName,
    DateOnly? Birthdate,
    string? ProfileImageUrl,
    string? ReferralCode,
    string? ReferredBy,
    decimal BonusBalance,
    string? Role,
    DateTime CreatedAt);
