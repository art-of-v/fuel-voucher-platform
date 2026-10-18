using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdateBrandStation;

public sealed record UpdateBrandStationCommand(Guid Id, BrandStation Updated);
