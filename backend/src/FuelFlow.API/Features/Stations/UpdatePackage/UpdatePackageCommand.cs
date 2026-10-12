using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Stations.UpdatePackage;

public sealed record UpdatePackageCommand(string Id, FuelPackage Updated);
