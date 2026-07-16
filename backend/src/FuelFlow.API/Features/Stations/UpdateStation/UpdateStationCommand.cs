using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Stations.UpdateStation;

public sealed record UpdateStationCommand(string Id, Station Updated);
