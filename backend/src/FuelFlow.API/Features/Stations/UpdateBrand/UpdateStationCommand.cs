using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdateBrand;

public sealed record UpdateBrandCommand(string Id, Brand Updated);
