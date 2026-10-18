using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdateBrand;

public sealed record UpdateBrandCommand(Guid Id, Brand Updated);
