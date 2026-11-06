namespace FuelFlow.Features.Auth.GetCurrentUser;

/// <summary>
/// The account the caller is logged in as.
/// </summary>
/// <remarks>
/// The property names ARE the API contract: the mobile app and the admin SPA read
/// <c>id</c>, <c>phone</c>, <c>firstName</c> and the rest off this body, and the names are
/// lower camel case because the API serializes with the web defaults. Renaming one of them
/// silently empties a field on somebody's screen.
/// <para>
/// <c>promoAccessAvailable</c> is here because this is the payload the app already fetches and
/// re-fetches on every mount and every return to the foreground, and the question it answers — whether
/// a second button belongs on the checkout — is a question about this account and not about the basket.
/// It is a hint to the UI and nothing more: the checkout refuses a promotional order for an account
/// without the grant whatever this says, so a stale copy of it cannot buy anything.
/// </para>
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
    DateTime CreatedAt,
    bool PromoAccessAvailable);
