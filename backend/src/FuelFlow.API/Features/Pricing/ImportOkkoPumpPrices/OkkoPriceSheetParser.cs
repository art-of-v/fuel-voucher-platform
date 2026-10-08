using System.Globalization;
using System.Text;

namespace FuelFlow.Features.Pricing.ImportOkkoPumpPrices;

/// <summary>
/// Parses the pump-price CSV produced by <c>scripts/fetch-okko-prices.mjs</c> into rows.
/// <para>
/// The column set is fixed and the header must name both columns, so a file scraped from the wrong
/// page (or a hand-typed sheet with different headings) is rejected outright instead of importing
/// rows of garbage prices. Money is parsed with <see cref="CultureInfo.InvariantCulture"/> because the
/// scraper writes <c>92.90</c> with a dot regardless of the operator's regional settings.
/// </para>
/// <para>
/// Parse errors are per-row: one malformed line never drops the whole file, mirroring
/// <c>StationNodeImportParser</c>, and every rejection is reported back with its line number.
/// </para>
/// </summary>
public static class OkkoPriceSheetParser
{
    public const string FuelCodeColumn = "fuelCode";
    public const string PriceColumn = "pricePerLiter";

    public static OkkoPriceSheetParseResult Parse(string content)
    {
        var result = new OkkoPriceSheetParseResult();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
        {
            result.Errors.Add(new OkkoPriceSheetError(0, "File is empty"));
            return result;
        }

        var header = SplitCsvLine(lines[headerIndex]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
            col[header[i].Trim()] = i;

        foreach (var required in new[] { FuelCodeColumn, PriceColumn })
        {
            if (!col.ContainsKey(required))
            {
                result.Errors.Add(new OkkoPriceSheetError(
                    headerIndex + 1, $"Missing required column '{required}'"));
                return result;
            }
        }

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var fields = SplitCsvLine(lines[i]);
            string? Get(string name) =>
                col.TryGetValue(name, out var idx) && idx < fields.Count ? fields[idx].Trim() : null;

            var fuelCode = Get(FuelCodeColumn);
            var priceRaw = Get(PriceColumn);

            if (string.IsNullOrWhiteSpace(fuelCode))
            {
                result.Errors.Add(new OkkoPriceSheetError(lineNumber, "fuelCode is required"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(priceRaw))
            {
                result.Errors.Add(new OkkoPriceSheetError(lineNumber, $"pricePerLiter is required for '{fuelCode}'"));
                continue;
            }

            if (!decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                result.Errors.Add(new OkkoPriceSheetError(lineNumber, $"Invalid pricePerLiter '{priceRaw}'"));
                continue;
            }
            // A zero or negative pump price would silently become the ceiling for every package of
            // that fuel and drive the customer price to zero — refuse it at the door.
            if (price <= 0m)
            {
                result.Errors.Add(new OkkoPriceSheetError(lineNumber, $"pricePerLiter must be greater than 0, got {priceRaw}"));
                continue;
            }

            result.Rows.Add(new OkkoPriceSheetRow(lineNumber, fuelCode, price));
        }

        return result;
    }

    /// <summary>Minimal RFC 4180 field splitter: honours double-quoted fields and "" escapes.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}

/// <summary>One parsed price row, carrying the source line for error reporting.</summary>
public sealed record OkkoPriceSheetRow(int Line, string FuelCode, decimal PricePerLiter);

/// <summary>A rejected row (or the file as a whole), with the 1-based line it came from.</summary>
public sealed record OkkoPriceSheetError(int Line, string Message);

public sealed class OkkoPriceSheetParseResult
{
    public List<OkkoPriceSheetRow> Rows { get; } = new();
    public List<OkkoPriceSheetError> Errors { get; } = new();
}