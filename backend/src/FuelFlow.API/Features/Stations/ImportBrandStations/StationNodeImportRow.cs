namespace FuelFlow.Features.Brands.ImportBrandStations;

/// <summary>
/// One parsed row from an uploaded АЗК file, before domain validation. Lat/Lng are strings at
/// parse time so a malformed number becomes a per-row error instead of failing the whole file.
/// </summary>
public sealed class BrandStationImportRow
{
    public int Line { get; set; }
    public string? Id { get; set; }
    public string? BrandId { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

/// <summary>A row that could not even be parsed (bad number, missing column count, malformed JSON).</summary>
public sealed record BrandStationImportError(int Line, string Message);

public sealed class BrandStationImportParseResult
{
    public List<BrandStationImportRow> Rows { get; } = new();
    public List<BrandStationImportError> Errors { get; } = new();
}
