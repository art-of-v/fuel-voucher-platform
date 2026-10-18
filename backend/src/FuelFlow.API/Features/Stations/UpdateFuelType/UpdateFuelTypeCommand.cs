using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Features.Brands.UpdateFuelType;

public sealed record UpdateFuelTypeCommand(string Id, FuelTypeEntity Updated, Guid ChangedByUserId);
