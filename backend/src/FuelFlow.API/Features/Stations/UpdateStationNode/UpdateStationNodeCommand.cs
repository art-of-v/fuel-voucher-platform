using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Stations.UpdateStationNode;

public sealed record UpdateStationNodeCommand(string Id, StationNode Updated);
