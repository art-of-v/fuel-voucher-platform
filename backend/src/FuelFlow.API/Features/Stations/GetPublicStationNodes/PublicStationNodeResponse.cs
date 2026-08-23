namespace FuelFlow.Features.Stations.GetPublicStationNodes;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.StationNode"/> for the
/// two anonymous, publicly cacheable <c>GET /api/station-nodes</c> endpoints.
///
/// <para>
/// FF-31 replaced the raw-entity response on <c>/api/stations</c> and <c>/api/stations/fuel-types</c>
/// with a DTO, but <c>StationNodeController</c> was left returning <c>List&lt;StationNode&gt;</c> - so
/// the hardening note claiming this was "done for the anonymous endpoints" was only partly true. The
/// exposure is the same one: these endpoints are anonymous and <c>[ResponseCache(Duration = 300)]</c>,
/// so any column later added to the station_nodes table would be published to unauthenticated callers,
/// and cached, with no change to any endpoint for a reviewer to notice.
/// </para>
///
/// <para>
/// Every field the entity exposes today is carried through except <c>CreatedAtUtc</c> and
/// <c>UpdatedAtUtc</c>, which no client reads, so the wire format is unchanged for installed mobile
/// clients.
/// </para>
/// </summary>
public sealed record PublicStationNodeResponse(
    string Id,
    string StationId,
    string Name,
    string? Address,
    string? Phone,
    string? City,
    string? StationType,
    double? Lat,
    double? Lng);
