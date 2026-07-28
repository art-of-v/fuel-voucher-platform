namespace FuelFlow.Features.Stations.FuelPrices.UpdateFuelPrice;

public sealed record UpdateFuelPriceCommand(
    string PackageId,
    Guid ChangedByUserId,
    decimal? SupplierPricePerLiter,
    decimal? MarginUahPerLiter,
    decimal? MarginPercent,
    decimal? FinalPricePerLiter
);
