namespace FuelFlow.Features.Stations.GetPublicFuelTypes;

/// <summary>
/// Explicit allow-list projection of <see cref="FuelFlow.SharedKernel.Domain.FuelTypeEntity"/> for the
/// anonymous, publicly cacheable <c>GET /api/stations/fuel-types</c> endpoint (FF-31).
///
/// That endpoint previously reused <c>GetAdminFuelTypesQueryHandler</c> and returned the entity, so any
/// column later added to the table would have been published to unauthenticated callers and cached for
/// 300 seconds with no code change to review. The fields below are exactly those the mobile client
/// declares in <c>mobile/src/core/types/api.ts</c>, so the wire format is unchanged; only the audit
/// timestamps, which no client reads, are dropped.
/// </summary>
public sealed record PublicFuelTypeResponse(
    string Id,
    string Name,
    string StationId,
    int BasePrice,
    int DiscountPrice);
