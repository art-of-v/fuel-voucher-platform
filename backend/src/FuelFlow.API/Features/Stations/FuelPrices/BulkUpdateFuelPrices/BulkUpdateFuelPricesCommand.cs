namespace FuelFlow.Features.Stations.FuelPrices.BulkUpdateFuelPrices;

public sealed record BulkUpdateFuelPricesCommand(
    Guid ChangedByUserId,
    List<BulkPricePatch> Patches
);

public sealed record BulkPricePatch(
    string PackageId,
    decimal? SupplierPricePerLiter,
    decimal? MarginUahPerLiter,
    decimal? MarginPercent,
    decimal? FinalPricePerLiter
);
