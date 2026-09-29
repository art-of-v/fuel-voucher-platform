namespace FuelFlow.Features.Stations.GetAdminStationNodes;

public sealed record GetAdminStationNodesQuery(int Page = 1, int PageSize = 50, string? StationId = null);
