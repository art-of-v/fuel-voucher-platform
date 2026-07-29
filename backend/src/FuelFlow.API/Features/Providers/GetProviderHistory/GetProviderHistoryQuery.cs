namespace FuelFlow.Features.Providers.GetProviderHistory;

public sealed record GetProviderHistoryQuery(string ProviderId, int Limit = 50);
