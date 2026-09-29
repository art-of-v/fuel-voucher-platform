using System.Globalization;
using System.Text.Json;

namespace FuelFlow.Features.Stations.ImportStationNodes;

/// <summary>
/// Parses an uploaded АЗК file (CSV or JSON) into rows. Format is chosen by content type / shape,
/// not trusted from the extension. Parse errors are per-row so one bad line never drops the file.
/// Ukrainian addresses contain commas ("Проспект Перемоги, 98"), so the CSV reader is quote-aware.
/// </summary>
public static class StationNodeImportParser
{
    public static StationNodeImportParseResult Parse(string content, StationNodeImportFormat format)
    {
        return format switch
        {
            StationNodeImportFormat.Json => ParseJson(content),
            _ => ParseCsv(content),
        };
    }

    private static StationNodeImportParseResult ParseJson(string content)
    {
        var result = new StationNodeImportParseResult();
        List<JsonRow>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<JsonRow>>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Errors.Add(new StationNodeImportError(0, $"Invalid JSON: {ex.Message}"));
            return result;
        }

        if (rows is null)
        {
            result.Errors.Add(new StationNodeImportError(0, "JSON payload was empty"));
            return result;
        }

        var line = 0;
        foreach (var row in rows)
        {
            line++;
            result.Rows.Add(new StationNodeImportRow
            {
                Line = line,
                Id = Trim(row.Id),
                StationId = Trim(row.StationId),
                Name = Trim(row.Name),
                Address = Trim(row.Address),
                Phone = Trim(row.Phone),
                City = Trim(row.City),
                StationType = Trim(row.StationType),
                Lat = row.Lat,
                Lng = row.Lng,
            });
        }

        return result;
    }

    private static StationNodeImportParseResult ParseCsv(string content)
    {
        var result = new StationNodeImportParseResult();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var headerIndex = Array.FindIndex(lines, l => !string.IsNullOrWhiteSpace(l));
        if (headerIndex < 0)
        {
            result.Errors.Add(new StationNodeImportError(0, "File is empty"));
            return result;
        }

        var header = SplitCsvLine(lines[headerIndex]);
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
            col[header[i].Trim()] = i;

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var fields = SplitCsvLine(lines[i]);
            string? Get(string name) =>
                col.TryGetValue(name, out var idx) && idx < fields.Count && !string.IsNullOrWhiteSpace(fields[idx])
                    ? fields[idx].Trim()
                    : null;

            var latRaw = Get("lat");
            var lngRaw = Get("lng");
            double? lat = null, lng = null;
            if (latRaw != null && !TryParseCoord(latRaw, out lat))
            {
                result.Errors.Add(new StationNodeImportError(lineNumber, $"Invalid lat '{latRaw}'"));
                continue;
            }
            if (lngRaw != null && !TryParseCoord(lngRaw, out lng))
            {
                result.Errors.Add(new StationNodeImportError(lineNumber, $"Invalid lng '{lngRaw}'"));
                continue;
            }

            result.Rows.Add(new StationNodeImportRow
            {
                Line = lineNumber,
                Id = Get("id"),
                StationId = Get("stationId"),
                Name = Get("name"),
                Address = Get("address"),
                Phone = Get("phone"),
                City = Get("city"),
                StationType = Get("stationType"),
                Lat = lat,
                Lng = lng,
            });
        }

        return result;
    }

    private static bool TryParseCoord(string raw, out double? value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Minimal RFC 4180 field splitter: honours double-quoted fields and "" escapes.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class JsonRow
    {
        public string? Id { get; set; }
        public string? StationId { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? StationType { get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
