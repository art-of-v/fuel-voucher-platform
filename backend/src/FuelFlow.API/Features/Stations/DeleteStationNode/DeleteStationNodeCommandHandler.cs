using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.DeleteStationNode;

public sealed class DeleteStationNodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteStationNodeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteStationNodeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.StationNodes
            .AsTracking()
            .FirstOrDefaultAsync(n => n.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove: a node is part of the station map other rows point at, so it is
        // history rather than garbage (#217). AsTracking matters - the context defaults to
        // NoTracking and the flag would not persist without it.
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
