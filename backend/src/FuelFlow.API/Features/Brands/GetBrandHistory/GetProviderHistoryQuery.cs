namespace FuelFlow.Features.Brands.GetProviderHistory;

public sealed record GetBrandHistoryQuery(string ScopeId, int Limit = 50);
