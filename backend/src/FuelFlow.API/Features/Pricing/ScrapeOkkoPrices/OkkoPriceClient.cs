using System.Globalization;
using System.Text.Json;
using FuelFlow.Features.Pricing.ImportOkkoPumpPrices;
using FuelFlow.SharedKernel.Options;
using Microsoft.Extensions.Options;

namespace FuelFlow.Features.Pricing.ScrapeOkkoPrices;

/// <summary>
/// Reads OKKO's published pump prices (колонка) from its public page API.
/// </summary>
public interface IOkkoPriceClient
{
    /// <summary>
    /// Fetches the current price list. Throws on transport failure, a non-success status, or a payload
    /// that carries no recognisable price rows — the caller must treat every one of those as "prices
    /// unknown" and leave the catalog alone rather than writing a half-read list.
    /// </summary>
    Task<IReadOnlyList<OkkoPumpPrice>> FetchAsync(CancellationToken ct = default);
}

/// <summary>
/// Fetches <c>okko.ua/api/{locale}/fuels</c> and extracts the fuel prices from it.
/// <para>
/// The API returns the same page component tree the website renders, so the prices sit in
/// <c>bullets.items[]</c> alongside the site's <c>fuel_code</c>. Rows are located by that shape rather
/// than by a hard-coded path: OKKO nests the tree differently per locale and changes it between page
/// revisions, and a hard-coded path would read as "no prices" instead of failing visibly. Only moving
/// the prices out of this component would break it, and that surfaces as a thrown
/// <see cref="OkkoPriceScrapeException"/> rather than as silent success.
/// </para>
/// <para>
/// Prices arrive as JSON strings ("92.90") with a dot as the only separator, never a comma, so they
/// are parsed with the invariant culture.
/// </para>
/// </summary>
public sealed class OkkoPriceClient : IOkkoPriceClient
{
    private readonly HttpClient _http;
    private readonly OkkoPriceOptions _options;
    private readonly ILogger<OkkoPriceClient> _logger;

    public OkkoPriceClient(
        HttpClient http,
        IOptions<OkkoPriceOptions> options,
        ILogger<OkkoPriceClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OkkoPumpPrice>> FetchAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.EndpointUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new OkkoPriceScrapeException(
                $"GET {_options.EndpointUrl} -> HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var payload = await response.Content.ReadAsStringAsync(ct);

        var prices = new List<OkkoPumpPrice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(payload);
        Collect(document.RootElement, prices, seen);

        if (prices.Count == 0)
        {
            throw new OkkoPriceScrapeException(
                "the response carried no rows with both fuel_code and price — OKKO changed the payload shape");
        }

        _logger.LogInformation(
            "Fetched {Count} OKKO price(s) from {Url}: {Prices}",
            prices.Count, _options.EndpointUrl,
            string.Join(", ", prices.Select(p => $"{p.SiteFuelCode}={p.PricePerLiter:F2}")));

        return prices;
    }

    /// <summary>
    /// Walks the JSON tree collecting every object that carries both a <c>fuel_code</c> and a
    /// <c>price</c>. Matching on the pair rather than on the path means an added or reordered
    /// component costs nothing, and a component that merely looks price-shaped (no code) is ignored.
    /// </summary>
    private static void Collect(JsonElement node, List<OkkoPumpPrice> into, HashSet<string> seen)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryRead(node, into, seen)) return;
                foreach (var property in node.EnumerateObject())
                {
                    Collect(property.Value, into, seen);
                }
                return;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    Collect(item, into, seen);
                }
                return;
        }
    }

    private static bool TryRead(JsonElement node, List<OkkoPumpPrice> into, HashSet<string> seen)
    {
        if (!node.TryGetProperty("fuel_code", out var codeElement)
            || !node.TryGetProperty("price", out var priceElement))
        {
            return false;
        }

        var code = codeElement.ValueKind == JsonValueKind.String
            ? codeElement.GetString()?.Trim()
            : codeElement.ToString();

        var priceText = priceElement.ValueKind == JsonValueKind.String
            ? priceElement.GetString()?.Trim()
            : priceElement.ToString();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(priceText))
        {
            return false;
        }

        if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            || price <= 0m)
        {
            return false;
        }

        // First wins: OKKO publishes one row per fuel, so a duplicate means the tree repeats the
        // component and taking the later copy would make the result depend on tree order.
        if (seen.Add(code)) into.Add(new OkkoPumpPrice(code, price));
        return true;
    }
}

/// <summary>Raised when OKKO's prices could not be read. Never leaves the catalog half-written.</summary>
public sealed class OkkoPriceScrapeException : Exception
{
    public OkkoPriceScrapeException(string message) : base(message) { }
    public OkkoPriceScrapeException(string message, Exception inner) : base(message, inner) { }
}