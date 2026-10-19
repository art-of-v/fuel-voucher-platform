using System.Text.Json;
using System.Text.RegularExpressions;

namespace FuelFlow.Features.Pricing.ScrapeUpgWogPrices;

/// <summary>
/// One grade as a vendor publishes it: the brand's own word for it, and what it charges per litre.
/// </summary>
public sealed record UpgWogPrice(string FuelCode, decimal PricePerLiter);

/// <summary>
/// Reads the fuel list UPG and WOG publish, as a list of (code, price).
/// </summary>
public interface IUpgWogPriceClient
{
    /// <param name="brand">The brand's slug: <c>upg</c> or <c>wog</c>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="UpgWogPriceScrapeException">
    /// The page could not be read, or carried no prices. Never a partial answer: a vendor page that
    /// has changed shape must leave the catalogue alone rather than write half of it.
    /// </exception>
    Task<IReadOnlyList<UpgWogPrice>> FetchAsync(string brand, CancellationToken ct = default);
}

/// <summary>
/// A vendor page that could not be read.
/// </summary>
public sealed class UpgWogPriceScrapeException : Exception
{
    public UpgWogPriceScrapeException(string message) : base(message) { }
    public UpgWogPriceScrapeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The two page shapes UPG and WOG publish, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <b>UPG publishes a JavaScript blob.</b> The page carries <c>let averagePrices = {"data":[…]}</c>
/// with one entry per grade: a <c>Title</c> that is the brand's own word for the grade, and a
/// <c>Price</c> that arrives with float noise - <c>49.899999999999999</c> - so it is rounded here
/// rather than written raw, because a price column carrying that is a number every downstream
/// reader has to know to round.
/// </para>
/// <para>
/// <b>WOG publishes a Next.js state.</b> The page is a Next.js app and the prices live in its Redux
/// state under <c>__NEXT_DATA__</c>. Three differences from the blob, and the third is the one that
/// bites: the grade is split across <c>type</c> and <c>brand</c>, the key is lowercase
/// <c>price</c>, and <b>the price is in kopecks</b>. <c>10650</c> read as UAH makes the catalogue a
/// hundred times too dear, and every downstream guard still passes, because a higher price is never
/// "below cost".
/// </para>
/// <para>
/// This is the JavaScript reader from <c>scripts/fetch-upg-wog-prices.mjs</c>, ported. The script
/// stays: it is the tool an operator uses to produce a file by hand, and the two are held to the
/// same contract - the two-column CSV the backend importer reads - and to the same fixtures.
/// </para>
/// </remarks>
public static partial class UpgWogPricePage
{
    /// <summary>100 kopecks to the hryvnia. WOG's state carries prices in kopecks.</summary>
    public const decimal KopecksPerUah = 100m;

    [GeneratedRegex(@"averagePrices\s*=\s*\{")]
    private static partial Regex AveragePricesMarker();

    [GeneratedRegex(@"__NEXT_DATA__[^>]*>")]
    private static partial Regex NextDataMarker();

    /// <summary>
    /// Every price the page carries, in the order it publishes them.
    /// </summary>
    /// <param name="html">The fetched page.</param>
    /// <exception cref="UpgWogPriceScrapeException">The page carries neither shape, or neither yields a price.</exception>
    public static IReadOnlyList<UpgWogPrice> Parse(string html)
    {
        // The blob is tried first because UPG publishes it verbatim, and it is the older, simpler
        // reader. WOG's page no longer carries it at all, so falling through to the Redux state is
        // not a fallback for a broken read - it is the only shape that brand has now.
        var blob = ExtractAveragePrices(html);
        if (blob is not null)
        {
            return FromBlob(blob);
        }

        var redux = ExtractNextDataFuels(html);
        if (redux.HasValue)
        {
            return FromRedux(redux.Value);
        }

        throw new UpgWogPriceScrapeException(
            "the page carried neither the averagePrices blob nor a __NEXT_DATA__ state - the vendor changed the page");
    }

    /// <summary>
    /// The JavaScript blob, extracted by its assignment rather than by a path: the marker is what
    /// survives a page redesign, and the surrounding markup changes and this must not.
    /// </summary>
    private static string? ExtractAveragePrices(string html)
    {
        var match = AveragePricesMarker().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var start = match.Index + match.Length - 1;
        var end = BalancedBraceEnd(html, start);
        return end < 0 ? null : html[start..(end + 1)];
    }

    /// <summary>
    /// The Next.js state, extracted the same way: the tag is the marker, the JSON after it is the
    /// state, and the braces are balanced by hand because the payload is not a document until the
    /// closing brace is found.
    /// </summary>
    private static JsonElement? ExtractNextDataFuels(string html)
    {
        var tag = NextDataMarker().Match(html);
        if (!tag.Success)
        {
            return null;
        }

        var start = html.IndexOf('{', tag.Index + tag.Length);
        if (start < 0)
        {
            return null;
        }

        var end = BalancedBraceEnd(html, start);
        if (end < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(html[start..(end + 1)]);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The index of the brace that closes the one at <paramref name="start"/>, or -1.
    /// </summary>
    /// <remarks>
    /// Counted by hand rather than by a JSON parser because the blob is not a document until the
    /// closing brace is found: it is a JavaScript object literal, with escapes and embedded
    /// strings, and a parser handed half of it says "invalid JSON" for a reason that is not the one
    /// that matters.
    /// </remarks>
    private static int BalancedBraceEnd(string html, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The blob's rows: <c>{"data":[{"Title":"…","Price":…}]}</c>.
    /// </summary>
    private static IReadOnlyList<UpgWogPrice> FromBlob(string blob)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(blob);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new UpgWogPriceScrapeException("the averagePrices blob was not valid JSON", ex);
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new UpgWogPriceScrapeException("the averagePrices blob carried no 'data' array");
        }

        var rows = new List<UpgWogPrice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("Title", out var titleElement))
            {
                continue;
            }

            var code = Collapse(titleElement.GetString());
            if (code.Length == 0)
            {
                continue;
            }

            if (!item.TryGetProperty("Price", out var priceElement) || !TryPrice(priceElement, out var price) || price <= 0m)
            {
                continue;
            }

            // The page repeating a component is not a second grade; a repeat at a DIFFERENT price is
            // worth saying out loud rather than silently keeping the first.
            if (!seen.Add(code))
            {
                continue;
            }

            rows.Add(new UpgWogPrice(code, Round2(price)));
        }

        if (rows.Count == 0)
        {
            throw new UpgWogPriceScrapeException("the averagePrices blob carried no grade with a price");
        }

        return rows;
    }

    /// <summary>
    /// WOG's rows, found by content rather than by path: <c>pageReducer.elements[].block.items[]</c>
    /// is one of several block layouts on the site, and a next.js release can insert a sibling. Every
    /// price row carries <c>i_am_page_element: "block.fuels_fuel"</c>, which is the same
    /// self-describing marker the rest of the page uses for its blocks, so it survives a re-layout
    /// that a fixed path would not.
    /// </summary>
    private static IReadOnlyList<UpgWogPrice> FromRedux(JsonElement state)
    {
        var items = new List<JsonElement>();
        CollectFuelItems(state, items, new HashSet<JsonElement>());

        var rows = new List<UpgWogPrice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            // The grade is `type` and `brand` joined, because that is how WOG writes it: "ДП" +
            // "Mustang+" is "ДП Mustang+", "95" + "Євро5-Е10" is "95 Євро5-Е10". Either half alone is
            // not the name the brand shows its customers, so neither is usable as the code.
            var type = Collapse(item.TryGetProperty("type", out var t) ? t.GetString() : null);
            var brand = Collapse(item.TryGetProperty("brand", out var b) ? b.GetString() : null);
            var code = Collapse(string.Join(' ', new[] { type, brand }.Where(part => part.Length > 0)));

            if (code.Length == 0)
            {
                continue;
            }

            if (!item.TryGetProperty("price", out var priceElement) || !TryPrice(priceElement, out var price) || price <= 0m)
            {
                continue;
            }

            if (!seen.Add(code))
            {
                continue;
            }

            rows.Add(new UpgWogPrice(code, Round2(price / KopecksPerUah)));
        }

        if (rows.Count == 0)
        {
            throw new UpgWogPriceScrapeException("the __NEXT_DATA__ state carried no grade with a price");
        }

        return rows;
    }

    private static void CollectFuelItems(JsonElement node, List<JsonElement> found, HashSet<JsonElement> seen)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("i_am_page_element", out var marker)
                    && marker.ValueKind == JsonValueKind.String
                    && marker.GetString() == "block.fuels_fuel"
                    && seen.Add(node))
                {
                    found.Add(node);
                }

                foreach (var property in node.EnumerateObject())
                {
                    CollectFuelItems(property.Value, found, seen);
                }

                break;

            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray())
                {
                    CollectFuelItems(child, found, seen);
                }

                break;
        }
    }

    /// <summary>
    /// Whitespace runs collapsed and trimmed: price labels arrive padded when read out of the
    /// server-rendered payload.
    /// </summary>
    private static string Collapse(string? value)
        => value is null ? string.Empty : WhitespaceRuns().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    /// <summary>
    /// A price as a number. The blob carries a JSON number, and it carries it with float noise.
    /// </summary>
    private static bool TryPrice(JsonElement element, out decimal price)
    {
        price = 0m;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            price = number;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            price = parsed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Two decimals, away from zero: the same scale and rounding as the stored price, so a displayed
    /// figure and a stored one cannot disagree by a rounding step.
    /// </summary>
    private static decimal Round2(decimal value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}