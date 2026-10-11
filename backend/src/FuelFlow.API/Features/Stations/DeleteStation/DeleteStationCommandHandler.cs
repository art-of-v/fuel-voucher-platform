using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.DeleteStation;

public sealed class DeleteStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteStationCommand command, CancellationToken ct = default)
    {
        var entity = await _context.Stations
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove: contracts and station_nodes cascade from here, and those point
        // toward vouchers, so removing a station could reach paid-for paper (#217).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
