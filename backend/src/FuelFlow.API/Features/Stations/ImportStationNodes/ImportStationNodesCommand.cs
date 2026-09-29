namespace FuelFlow.Features.Stations.ImportStationNodes;

public sealed record ImportStationNodesCommand(string Content, StationNodeImportFormat Format);

public sealed class ImportStationNodesResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public List<StationNodeImportError> Errors { get; } = new();
}
