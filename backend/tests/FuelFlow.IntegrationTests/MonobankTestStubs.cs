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

    /// <summary>Every merchant the production code asked for, in call order.</summary>
    /// <remarks>
    /// Recording is what lets a test assert which merchant was polled. The reconciliation tests
    /// need it: recovery must query the merchant that issued the invoice, because polling the live
    /// merchant for a sandbox invoice answers "unknown invoice" and cancels an order whose payment
    /// may be sitting there. Tests that do not care simply ignore the list.
    /// </remarks>
    public List<MonobankMerchant> RequestedMerchants { get; } = [];

    public IMonobankClient ForMerchant(MonobankMerchant merchant)
    {
        RequestedMerchants.Add(merchant);
        return _client;
    }
}
