using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdatePackage;

public sealed record UpdatePackageCommand(string Id, FuelPackage Updated);
