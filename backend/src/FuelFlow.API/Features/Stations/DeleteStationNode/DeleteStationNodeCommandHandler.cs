using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.DeleteStationNode;

public sealed class DeleteStationNodeCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteStationNodeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteStationNodeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.StationNodes.FirstOrDefaultAsync(n => n.Id == command.Id, ct);
        if (entity is null) return false;

        _context.StationNodes.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
