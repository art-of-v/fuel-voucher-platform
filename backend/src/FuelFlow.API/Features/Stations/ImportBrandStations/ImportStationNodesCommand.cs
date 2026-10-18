namespace FuelFlow.Features.Brands.ImportBrandStations;

public sealed record ImportBrandStationsCommand(string Content, BrandStationImportFormat Format);

public sealed class ImportBrandStationsResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public List<BrandStationImportError> Errors { get; } = new();
}
