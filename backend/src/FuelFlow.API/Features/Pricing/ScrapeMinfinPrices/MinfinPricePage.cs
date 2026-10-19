using System.Globalization;
using System.Text.RegularExpressions;

namespace FuelFlow.Features.Pricing.ScrapeMinfinPrices;

/// <summary>One grade as Minfin publishes it: the grade's name, and what the network charges for it.</summary>
public sealed record MinfinFuelPrice(string Grade, decimal PricePerLiter);

/// <summary>
/// Reads the average retail prices Minfin publishes for the fuel networks.
/// </summary>
public interface IMinfinPriceClient
{
    /// <param name="network">The network's slug as Minfin spells it: <c>klo</c>, <c>okko</c>, <c>wog</c>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="MinfinPriceScrapeException">
    /// The page could not be read, or the network is not in it. Never a partial answer: a page that
    /// has changed shape must leave the catalogue alone rather than write half a table.
    /// </exception>
    Task<IReadOnlyList<MinfinFuelPrice>> FetchAsync(string network, CancellationToken ct = default);
}

/// <summary>
/// A Minfin page that could not be read.
/// </summary>
public sealed class MinfinPriceScrapeException : Exception
{
    public MinfinPriceScrapeException(string message) : base(message) { }
    public MinfinPriceScrapeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Minfin's fuel price table.
/// </summary>
/// <remarks>
/// <para>
/// One row per network, five columns of grades: <c>А 95+</c>, <c>А 95</c>, <c>А 92</c>, <c>ДП</c> and
/// <c>Газ</c>. The grades are the generic ones, not each network's own vocabulary - WOG calls a grade
/// "Ventus 95" and Minfin calls the same thing "А 95".
/// </para>
/// <para>
/// <b>Two things about the numbers.</b> The prices are written with a COMMA as the decimal
/// separator, which is not what any money parser in this codebase expects, and the table is dated -
/// the caption says which day the averages are for, because a price from last Tuesday is not a price.
/// </para>
/// <para>
/// This is the source for KLO, which publishes no prices of its own. It is also the reason our KLO
/// catalogue holds the same five grades Minfin lists: an operator reading this table names a fuel
/// after the column, and the import does the same.
/// </para>
/// </remarks>
public static partial class MinfinPricePage
{
    /// <summary>
    /// The grades Minfin's table has, in the order the columns are in.
    /// </summary>
    public static readonly string[] Grades = ["А 95+", "А 95", "А 92", "ДП", "Газ"];

    [GeneratedRegex(@"<a href='/ua/markets/fuel/tm/(?<slug>[a-z0-9_\-]+)/'>", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkLink();

    [GeneratedRegex(@"<tr[^>]*>(?<row>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"<td[^>]*>(?<cell>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableCell();

    [GeneratedRegex(@"<caption>(?<caption>.*?)</caption>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Caption();

    /// <summary>
    /// The prices one network is listed at, in the table's column order.
    /// </summary>
    /// <param name="html">The fetched page.</param>
    /// <param name="network">The network's slug as Minfin spells it.</param>
    /// <exception cref="MinfinPriceScrapeException">The page carries no such network, or its row carries no prices.</exception>
    public static IReadOnlyList<MinfinFuelPrice> Parse(string html, string network)
    {
        var slug = network.Trim().ToLowerInvariant();
        var found = FindRow(html, slug);
        if (found is null)
        {
            throw new MinfinPriceScrapeException($"the table carries no row for the network '{network}'");
        }

        var cells = TableCell().Matches(found)
            .Select(m => Strip(m.Groups["cell"].Value))
            .ToList();

        // The first cell is the network's name and the second is its logo; the prices follow.
        var prices = cells.Skip(2).ToList();
        if (prices.Count < Grades.Length)
        {
            throw new MinfinPriceScrapeException(
                $"the row for '{network}' carried {prices.Count} price cell(s), and the table has {Grades.Length} grade column(s)");
        }

        var result = new List<MinfinFuelPrice>();
        for (var i = 0; i < Grades.Length; i++)
        {
            if (!TryPrice(prices[i], out var price))
            {
                // A network that does not sell a grade leaves the cell empty. That is a fact about
                // the network, not a broken page.
                continue;
            }

            result.Add(new MinfinFuelPrice(Grades[i], price));
        }

        if (result.Count == 0)
        {
            throw new MinfinPriceScrapeException($"the row for '{network}' carried no price at all");
        }

        return result;
    }

    /// <summary>
    /// The day the table is dated, as it writes it. Reported so a price from last Tuesday is
    /// visible as one.
    /// </summary>
    public static string? DateOf(string html)
    {
        var caption = Caption().Match(html);
        if (!caption.Success)
        {
            return null;
        }

        var date = Regex.Match(caption.Groups["caption"].Value, @"\d{2}\.\d{2}\.\d{4}");
        return date.Success ? date.Value : null;
    }

    private static string? FindRow(string html, string slug)
    {
        foreach (Match row in TableRow().Matches(html))
        {
            var body = row.Groups["row"].Value;
            var link = NetworkLink().Match(body);
            if (link.Success && link.Groups["slug"].Value.Equals(slug, StringComparison.OrdinalIgnoreCase))
            {
                return body;
            }
        }

        return null;
    }

    /// <summary>
    /// A cell's text, without its markup.
    /// </summary>
    private static string Strip(string cell)
    {
        var text = Regex.Replace(cell, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ").Replace("&shy;", "").Replace("&amp;", "&");
        return WhitespaceRuns().Replace(text, " ").Trim();
    }

    /// <summary>
    /// A price as a number. Minfin writes the decimal separator as a COMMA, and a money parser that
    /// expects a dot would read "95,60" as ninety-five.
    /// </summary>
    private static bool TryPrice(string cell, out decimal price)
    {
        price = 0m;
        var text = cell.Replace(" ", "").Replace(" ", "").Trim();
        if (text.Length == 0)
        {
            return false;
        }

        return decimal.TryParse(
            text,
            NumberStyles.AllowDecimalPoint,
            new CultureInfo("uk-UA") { NumberFormat = { NumberDecimalSeparator = "," } },
            out price)
            || decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();
}
