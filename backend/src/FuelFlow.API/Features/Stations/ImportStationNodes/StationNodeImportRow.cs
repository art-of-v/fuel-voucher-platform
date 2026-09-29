namespace FuelFlow.Features.Stations.ImportStationNodes;

/// <summary>
/// One parsed row from an uploaded АЗК file, before domain validation. Lat/Lng are strings at
/// parse time so a malformed number becomes a per-row error instead of failing the whole file.
/// </summary>
public sealed class StationNodeImportRow
{
    public int Line { get; set; }
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

/// <summary>A row that could not even be parsed (bad number, missing column count, malformed JSON).</summary>
public sealed record StationNodeImportError(int Line, string Message);

public sealed class StationNodeImportParseResult
{
    public List<StationNodeImportRow> Rows { get; } = new();
    public List<StationNodeImportError> Errors { get; } = new();
}
