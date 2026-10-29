using System.Net.Http;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Pricing.ScrapeUpgWogPrices;

/// <summary>
/// Fetches the vendor's fuel page and turns it into the two-column CSV the backend importer reads.
/// </summary>
/// <remarks>
/// The CSV is the same contract <c>scripts/fetch-upg-wog-prices.mjs</c> writes, so the automatic
/// import and the file an operator uploads by hand go through ONE parser
/// (<c>OkkoPriceSheetParser</c>) and cannot disagree about what a row means.
/// </remarks>
public sealed class UpgWogPriceClient : IUpgWogPriceClient
{
    private readonly HttpClient _http;
    private readonly UpgWogPriceOptions _options;
    private readonly ILogger<UpgWogPriceClient> _logger;

    public UpgWogPriceClient(
        HttpClient http,
        IOptions<UpgWogPriceOptions> options,
        ILogger<UpgWogPriceClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UpgWogPrice>> FetchAsync(string brand, CancellationToken ct = default)
    {
        var url = _options.EndpointFor(brand);
        if (url is null)
        {
            throw new UpgWogPriceScrapeException($"No endpoint is configured for the brand '{brand}'.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "text/html");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpgWogPriceScrapeException(
                $"GET {url} -> HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // A 2xx with no body is not a page: on 2026-10-29 the WOG host answered 204 after a
        // redirect and the parse failed with "the page carried neither shape", which reads like the
        // vendor changed the markup. Say what actually arrived instead.
        var html = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new UpgWogPriceScrapeException(
                $"GET {url} -> HTTP {(int)response.StatusCode} with an empty body");
        }

        // The page is followed through redirects by the handler, so the URL the prices came from is
        // not necessarily the one configured. Said out loud when it differs: a vendor that moves the
        // page is the beginning of the next outage, and this line is the one that names it.
        var finalUrl = response.RequestMessage?.RequestUri?.ToString();
        if (!string.IsNullOrEmpty(finalUrl) && !string.Equals(finalUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "{Brand} price page moved: {ConfiguredUrl} answered {FinalUrl}. Update UpgWogPrice:{OptionName}.",
                brand, url, finalUrl, brand == "wog" ? "WogEndpointUrl" : "UpgEndpointUrl");
        }

        var prices = UpgWogPricePage.Parse(html);

        _logger.LogInformation(
            "Fetched {Count} {Brand} price(s) from {Url}: {Prices}",
            prices.Count, brand, url,
            string.Join(", ", prices.Select(p => $"{p.FuelCode}={p.PricePerLiter:F2}")));

        return prices;
    }
}

/// <summary>
/// The two-column CSV the UPG/WOG importer reads: <c>fuelCode,pricePerLiter</c>, CRLF-terminated,
/// RFC 4180 quoted.
/// </summary>
/// <remarks>
/// The rules are the ones in <c>scripts/lib/upg-wog-price-csv.mjs</c>, which says they must stay in
/// lockstep with <c>OkkoPriceSheetParser</c> in the backend - the same pairing this file is part
/// of, now that both halves live here.
/// </remarks>
public static partial class UpgWogPriceSheet
{
    public static readonly string[] Columns = ["fuelCode", "pricePerLiter"];

    /// <summary>
    /// The sheet as the importer's parser expects it.
    /// </summary>
    public static string Write(IEnumerable<UpgWogPrice> prices)
    {
        var lines = new List<string> { string.Join(',', Columns) };
        lines.AddRange(prices.Select(p => string.Join(',', Field(p.FuelCode), Field(p.PricePerLiter.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))));
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// RFC 4180 quoting - the importer's splitter honours quotes and "" escapes.
    /// </summary>
    private static string Field(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var text = WhitespaceRuns().Replace(value, " ").Trim();
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex WhitespaceRuns();
}