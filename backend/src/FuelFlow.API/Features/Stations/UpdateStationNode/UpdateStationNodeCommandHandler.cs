using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.UpdateStationNode;

public sealed class UpdateStationNodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public UpdateStationNodeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<UpdateStationNodeResult> HandleAsync(UpdateStationNodeCommand command, CancellationToken ct = default)
    {
        var updated = command.Updated;

        if (string.IsNullOrWhiteSpace(updated.StationId) || string.IsNullOrWhiteSpace(updated.Name))
            return new UpdateStationNodeResult { Error = "StationId and Name are required" };

        if (updated.Lat is null || updated.Lng is null ||
            !StationNodeCoordinates.IsValidLat(updated.Lat.Value) ||
            !StationNodeCoordinates.IsValidLng(updated.Lng.Value))
            return new UpdateStationNodeResult { Error = "Valid Lat (-90..90) and Lng (-180..180) are required" };

        var entity = await _context.StationNodes.FirstOrDefaultAsync(n => n.Id == command.Id, ct);
        if (entity is null) return new UpdateStationNodeResult { NotFound = true };

        var stationExists = await _context.Stations.AnyAsync(s => s.Id == updated.StationId, ct);
        if (!stationExists)
            return new UpdateStationNodeResult { Error = $"Station (brand) '{updated.StationId}' does not exist" };

        entity.StationId = updated.StationId;
        entity.Name = updated.Name;
        entity.Address = updated.Address;
        entity.Phone = updated.Phone;
        entity.City = updated.City;
        entity.StationType = updated.StationType;
        entity.Lat = updated.Lat;
        entity.Lng = updated.Lng;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        _context.StationNodes.Update(entity);

        await _context.SaveChangesAsync(ct);
        return new UpdateStationNodeResult { Success = true };
    }
}

public sealed class UpdateStationNodeResult
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public string? Error { get; set; }
}
