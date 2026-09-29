using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.CreateStationNode;

public sealed class CreateStationNodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public CreateStationNodeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<CreateStationNodeResult> HandleAsync(CreateStationNodeCommand command, CancellationToken ct = default)
    {
        var node = command.Node;

        if (string.IsNullOrWhiteSpace(node.Id) ||
            string.IsNullOrWhiteSpace(node.StationId) ||
            string.IsNullOrWhiteSpace(node.Name))
            return new CreateStationNodeResult { Error = "Id, StationId and Name are required" };

        if (node.Lat is null || node.Lng is null ||
            !StationNodeCoordinates.IsValidLat(node.Lat.Value) ||
            !StationNodeCoordinates.IsValidLng(node.Lng.Value))
            return new CreateStationNodeResult { Error = "Valid Lat (-90..90) and Lng (-180..180) are required" };

        var stationExists = await _context.Stations.AnyAsync(s => s.Id == node.StationId, ct);
        if (!stationExists)
            return new CreateStationNodeResult { Error = $"Station (brand) '{node.StationId}' does not exist" };

        var exists = await _context.StationNodes.AnyAsync(n => n.Id == node.Id, ct);
        if (exists)
            return new CreateStationNodeResult { Conflict = true, Error = $"Station node with id '{node.Id}' already exists" };

        node.CreatedAtUtc = DateTime.UtcNow;
        node.UpdatedAtUtc = node.CreatedAtUtc;
        _context.StationNodes.Add(node);
        await _context.SaveChangesAsync(ct);
        return new CreateStationNodeResult { Success = true, Node = node };
    }
}

public sealed class CreateStationNodeResult
{
    public bool Success { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public StationNode? Node { get; set; }
}
