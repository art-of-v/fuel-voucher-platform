using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdateBrandStation;

public sealed record UpdateBrandStationCommand(string Id, BrandStation Updated);
