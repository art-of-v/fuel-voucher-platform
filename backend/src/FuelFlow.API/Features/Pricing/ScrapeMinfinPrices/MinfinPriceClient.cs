using System.Net.Http;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Pricing.ScrapeMinfinPrices;

/// <summary>
/// Fetches Minfin's fuel price table and reads one network's row out of it.
/// </summary>
public sealed class MinfinPriceClient : IMinfinPriceClient
{
    private readonly HttpClient _http;
    private readonly MinfinPriceOptions _options;
    private readonly ILogger<MinfinPriceClient> _logger;

    public MinfinPriceClient(
        HttpClient http,
        IOptions<MinfinPriceOptions> options,
        ILogger<MinfinPriceClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MinfinFuelPrice>> FetchAsync(string network, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.EndpointUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MinfinPriceScrapeException(
                $"GET {_options.EndpointUrl} -> HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        var prices = MinfinPricePage.Parse(html, network);

        _logger.LogInformation(
            "Fetched {Count} {Network} price(s) from {Url}: {Prices} (table dated {Date})",
            prices.Count, network, _options.EndpointUrl,
            string.Join(", ", prices.Select(p => $"{p.Grade}={p.PricePerLiter:F2}").Take(6)),
            MinfinPricePage.DateOf(html) ?? "unknown");

        return prices;
    }
}
