namespace FuelFlow.Features.Stations.GetPublicPackagesByStation;

/// <param name="CallerUserId">The signed-in caller, or null — see <see cref="GetPublicPackagesQuery.CallerUserId"/>.</param>
public sealed record GetPublicPackagesByStationQuery(string StationId, Guid? CallerUserId);