namespace FuelFlow.Features.Brands.GetPublicBrands;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.Brand"/> for the
/// anonymous, publicly cacheable <c>GET /api/Brands</c> endpoint (FF-31).
///
/// Returning the entity directly meant any column later added to the table would be published to
/// unauthenticated callers - and cached for 300 seconds - with no code change to review. The fields
/// below are exactly those the mobile client declares in <c>mobile/src/core/types/api.ts</c> (plus
/// <c>sortOrder</c>, added alongside the client's type), so the wire format only grows deliberately;
/// audit timestamps, which no client reads, are dropped.
/// </para>
/// <para>
/// <see cref="Slug"/> was added when the id became a uuid (planning D-21). The client still needs a
/// stable, human-readable handle for a network — to label cached stock, to group a shelf offline, and
/// to read a log — and the id alone cannot serve any of those.
/// </para>
/// </remarks>
public sealed record PublicBrandResponse(
    Guid Id,
    string Slug,
    string Name,
    string Color,
    string LogoText,
    int SortOrder);
