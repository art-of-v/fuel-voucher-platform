using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.API.Features.Orders.SharedServices.Monobank;

/// <summary>
/// Hands out a Monobank client bound to one merchant's credentials.
/// </summary>
public interface IMonobankClientFactory
{
    /// <summary>
    /// A client for <paramref name="merchant"/>.
    /// </summary>
    /// <exception cref="MonobankMerchantUnavailableException">
    /// The merchant has no token configured. Raised rather than returning a client for the other
    /// merchant, because a caller asking for the sandbox and silently getting live is how test
    /// money becomes real money.
    /// </exception>
    IMonobankClient ForMerchant(MonobankMerchant merchant);
}

/// <summary>
/// Builds <see cref="MonobankClient"/> instances, one per merchant.
/// </summary>
/// <remarks>
/// The client used to read the token straight from <see cref="MonobankOptions"/>, which is why
/// there could only ever be one merchant. Binding the credentials at construction is what makes
/// a second one possible; nothing else about the client changes.
/// </remarks>
public sealed class MonobankClientFactory : IMonobankClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<MonobankOptions> _options;
    private readonly ILogger<MonobankClient> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public MonobankClientFactory(
        IHttpClientFactory httpClientFactory,
        IOptions<MonobankOptions> options,
        ILogger<MonobankClient> logger,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public IMonobankClient ForMerchant(MonobankMerchant merchant)
    {
        // Monobank:Enabled=false switches the whole integration off, which is what dev and
        // demo environments run. No real money can move in that mode, so routing is moot -
        // the in-process mock keeps checkout, refund and refund-sync exercisable without a
        // token. The fail-closed rules below only apply to a live integration.
        if (!_options.Value.Enabled)
        {
            return new MockMonobankClient(_options, _loggerFactory.CreateLogger<MockMonobankClient>());
        }

        var credentials = _options.Value.CredentialsFor(merchant);

        if (credentials is null || string.IsNullOrWhiteSpace(credentials.Token))
        {
            throw new MonobankMerchantUnavailableException(merchant);
        }

        // A fresh HttpClient per call: MonobankClient sets BaseAddress and the X-Token header on
        // it, so sharing one would leak the first merchant's token to the second.
        var httpClient = _httpClientFactory.CreateClient($"monobank-{merchant}");

        return new MonobankClient(_options, httpClient, _logger, credentials, merchant);
    }
}