namespace FuelFlow.Features.Stations.GetPublicStations;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.Station"/> for the
/// anonymous, publicly cacheable <c>GET /api/stations</c> endpoint (FF-31).
///
/// Returning the entity directly meant any column later added to the table would be published to
/// unauthenticated callers — and cached for 300 seconds — with no code change to review. The fields
/// below are exactly those the mobile client declares in <c>mobile/src/core/types/api.ts</c>, so the
/// wire format is unchanged; only the audit timestamps, which no client reads, are dropped.
/// </summary>
public sealed record PublicStationResponse(
    string Id,
    string Name,
    string Color,
    string LogoText,
    string? Address,
    string? Phone,
    string? StationType,
    double? Lat,
    double? Lng);
