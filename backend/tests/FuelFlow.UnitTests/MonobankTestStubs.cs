using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.SharedKernel.Options;

namespace FuelFlow.UnitTests;

/// <summary>
/// Test double for <see cref="IMonobankClientFactory"/>: hands out one shared client for every
/// merchant and records which merchants were requested, so a test can assert that a money path
/// (refund, reconciliation, refund sync) used the merchant persisted on the order rather than a
/// global default.
/// </summary>
public sealed class StubMonobankClientFactory : IMonobankClientFactory
{
    private readonly IMonobankClient _client;

    public StubMonobankClientFactory(IMonobankClient client) => _client = client;

    /// <summary>Every merchant the production code asked for, in call order.</summary>
    public List<MonobankMerchant> RequestedMerchants { get; } = [];

    public IMonobankClient ForMerchant(MonobankMerchant merchant)
    {
        RequestedMerchants.Add(merchant);
        return _client;
    }
}

/// <summary>
/// Test double for <see cref="IMonobankMerchantResolver"/>: answers with a fixed merchant (Live
/// by default, matching "a regular customer pays the live merchant") and records what the
/// production code asked, so a test can assert which phone/flag pair drove the decision.
/// </summary>
public sealed class StubMonobankMerchantResolver : IMonobankMerchantResolver
{
    private readonly Func<string?, bool, MonobankMerchant> _resolve;

    public StubMonobankMerchantResolver(MonobankMerchant merchant = MonobankMerchant.Live)
        : this((_, _) => merchant)
    {
    }

    public StubMonobankMerchantResolver(Func<string?, bool, MonobankMerchant> resolve) => _resolve = resolve;

    /// <summary>Every (phone, isQaAccount) pair the production code resolved, in call order.</summary>
    public List<(string? Phone, bool IsQaAccount)> Calls { get; } = [];

    public MonobankMerchant Resolve(string? phoneNumber, bool isQaAccount)
    {
        Calls.Add((phoneNumber, isQaAccount));
        return _resolve(phoneNumber, isQaAccount);
    }
}
