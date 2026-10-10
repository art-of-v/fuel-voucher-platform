using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Test double for <see cref="IMonobankClientFactory"/>: hands out one shared client for every
/// merchant, so integration tests keep exercising the real handlers while the routing work
/// itself is covered by unit tests against the real factory/resolver.
/// </summary>
public sealed class StubMonobankClientFactory : IMonobankClientFactory
{
    private readonly IMonobankClient _client;

    public StubMonobankClientFactory(IMonobankClient client) => _client = client;

    public IMonobankClient ForMerchant(MonobankMerchant merchant) => _client;
}
