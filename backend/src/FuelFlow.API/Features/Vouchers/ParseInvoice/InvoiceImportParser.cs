using System.Globalization;
using ClosedXML.Excel;

namespace FuelFlow.Features.Vouchers.ParseInvoice;

/// <summary>
/// Reads a supplier invoice (накладна) .xlsx into fuel line items. The column layout is discovered
/// from a header row by keyword — a real supplier export can be fed in with light massaging rather
/// than a rigid template. Ukrainian number cells ("48,50", "242 500,00", non-breaking spaces) are
/// normalised. One unreadable row is reported, never fatal (pricing epic RQ-3).
/// </summary>
public static class InvoiceImportParser
{
    // Header keywords (lower-cased, accent-free substrings). Price is probed before quantity so
    // "Ціна за літр" claims the price column, not the liters one.
    private static readonly string[] LabelKeys = ["паливо", "пальне", "продукт", "номенклатура", "найменування", "назва", "товар"];
    private static readonly string[] CodeKeys = ["код", "артикул"];
    private static readonly string[] PriceKeys = ["ціна", "варт"];
    private static readonly string[] TotalKeys = ["сума", "всього", "разом"];
    private static readonly string[] QuantityKeys = ["к-сть", "кільк", "обсяг", "літр", "литр", "обєм"];

    public static InvoiceParseResult Parse(Stream xlsx)
    {
        var result = new InvoiceParseResult();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(xlsx);
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ParseInvoiceIssue(0, $"Not a readable .xlsx workbook: {ex.Message}"));
            return result;
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet?.RangeUsed() is null)
            {
                result.Errors.Add(new ParseInvoiceIssue(0, "The workbook has no data"));
                return result;
            }

            var rows = sheet.RowsUsed().ToList();

            ColumnMap? map = null;
            var headerRow = 0;
            foreach (var row in rows)
            {
                var candidate = TryMapHeader(row);
                // A usable header names the fuel and gives a way to get a per-liter cost.
                if (candidate.Label > 0 && (candidate.CostPerLiter > 0 || (candidate.Total > 0 && candidate.Quantity > 0)))
                {
                    map = candidate;
                    headerRow = row.RowNumber();
                    break;
                }
            }

            if (map is null)
            {
                result.Errors.Add(new ParseInvoiceIssue(0,
                    "No header row found — expected a fuel column plus a price (or total + quantity) column"));
                return result;
            }

            foreach (var row in rows.Where(r => r.RowNumber() > headerRow))
            {
                var label = ReadText(row, map.Label);
                if (string.IsNullOrWhiteSpace(label)) continue; // spacer / totals / blank rows

                var liters = ReadNumber(row, map.Quantity);
                var cost = ReadNumber(row, map.CostPerLiter);
                var total = ReadNumber(row, map.Total);

                if (cost is null && total is null)
                {
                    result.Errors.Add(new ParseInvoiceIssue(row.RowNumber(), $"'{label.Trim()}': no price or total to read a cost from"));
                    continue;
                }

                result.Lines.Add(new InvoiceLine
                {
                    Line = row.RowNumber(),
                    Label = label.Trim(),
                    Code = NullIfBlank(ReadText(row, map.Code)),
                    Liters = liters,
                    CostPerLiter = cost,
                    Total = total,
                });
            }
        }

        if (result.Lines.Count == 0 && result.Errors.Count == 0)
            result.Errors.Add(new ParseInvoiceIssue(0, "No fuel line items were found below the header"));

        return result;
    }

    private static ColumnMap TryMapHeader(IXLRow row)
    {
        var map = new ColumnMap();
        foreach (var cell in row.CellsUsed())
        {
            var text = Fold(cell.GetString());
            if (text.Length == 0) continue;
            var col = cell.Address.ColumnNumber;

            if (map.Label == 0 && ContainsAny(text, LabelKeys)) map.Label = col;
            else if (map.CostPerLiter == 0 && ContainsAny(text, PriceKeys)) map.CostPerLiter = col;
            else if (map.Total == 0 && ContainsAny(text, TotalKeys)) map.Total = col;
            else if (map.Quantity == 0 && ContainsAny(text, QuantityKeys)) map.Quantity = col;
            else if (map.Code == 0 && ContainsAny(text, CodeKeys)) map.Code = col;
        }
        return map;
    }

    private static string? ReadText(IXLRow row, int col)
    {
        if (col <= 0) return null;
        var cell = row.Cell(col);
        return cell.IsEmpty() ? null : cell.GetString();
    }

    private static decimal? ReadNumber(IXLRow row, int col)
    {
        if (col <= 0) return null;
        var cell = row.Cell(col);
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number)
            return (decimal)cell.GetDouble();
        return ParseNumberString(cell.GetString());
    }

    /// <summary>
    /// Parse a Ukrainian-formatted number: keep only digits, sign and separators (dropping spaces,
    /// non-breaking spaces and currency marks), then treat the comma as the decimal point.
    /// </summary>
    internal static decimal? ParseNumberString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var kept = new string(raw.Where(c => char.IsDigit(c) || c is '.' or ',' or '-' or '+').ToArray());
        var cleaned = kept.Replace(",", ".");

        // A stray thousands dot/comma could leave two separators; only the last one is the decimal.
        var lastDot = cleaned.LastIndexOf('.');
        if (lastDot >= 0 && cleaned.IndexOf('.') != lastDot)
            cleaned = cleaned[..lastDot].Replace(".", string.Empty) + cleaned[lastDot..];

        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static bool ContainsAny(string text, string[] keys) => keys.Any(text.Contains);

    /// <summary>Lower-case and drop apostrophes/quotes so "об'єм" and "обєм" fold together.</summary>
    private static string Fold(string s) =>
        s.Trim().ToLowerInvariant().Replace("'", string.Empty).Replace("’", string.Empty).Replace("`", string.Empty);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed class ColumnMap
    {
        public int Label;
        public int Code;
        public int CostPerLiter;
        public int Total;
        public int Quantity;
    }
}
