namespace FuelFlow.Features.Stations.FuelPrices.GetFuelPriceAudit;

public sealed record GetFuelPriceAuditQuery(string? PackageId, int Limit = 100);
