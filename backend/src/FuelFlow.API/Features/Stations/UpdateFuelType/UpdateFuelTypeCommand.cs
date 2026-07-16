using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Stations.UpdateFuelType;

public sealed record UpdateFuelTypeCommand(string Id, FuelTypeEntity Updated);
