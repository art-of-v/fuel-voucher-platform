namespace FuelFlow.Features.Brands.GetPublicBrandStations;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.BrandStation"/> for the
/// two anonymous, publicly cacheable <c>GET /api/Brand-nodes</c> endpoints.
///
/// <para>
/// FF-31 replaced the raw-entity response on <c>/api/Brands</c> and <c>/api/Brands/fuel-types</c>
/// with a DTO, but <c>BrandStationController</c> was left returning <c>List&lt;BrandStation&gt;</c> - so
/// the hardening note claiming this was "done for the anonymous endpoints" was only partly true. The
/// exposure is the same one: these endpoints are anonymous and <c>[ResponseCache(Duration = 300)]</c>,
/// so any column later added to the Brand_nodes table would be published to unauthenticated callers,
/// and cached, with no change to any endpoint for a reviewer to notice.
/// </para>
///
/// <para>
/// Every field the entity exposes today is carried through except <c>CreatedAtUtc</c> and
/// <c>UpdatedAtUtc</c>, which no client reads, so the wire format is unchanged for installed mobile
/// clients.
/// </para>
///
/// <para>
/// <c>Id</c> and <c>BrandId</c> became uuids with planning D-21, and <c>BrandSlug</c> was added so a
/// client can still label a node without a second lookup. <c>ExternalId</c> is deliberately
/// <em>not</em> published: it is the importer's re-import key, and no client has any use for it.
/// </para>
/// </summary>
public sealed record PublicBrandStationResponse(
    Guid Id,
    Guid BrandId,
    string BrandSlug,
    string Name,
    string? Address,
    string? Phone,
    string? City,
    string? StationType,
    double? Lat,
    double? Lng);
