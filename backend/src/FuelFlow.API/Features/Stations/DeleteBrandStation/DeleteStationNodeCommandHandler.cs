using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.DeleteBrandStation;

public sealed class DeleteBrandStationCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteBrandStationCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteBrandStationCommand command, CancellationToken ct = default)
    {
        var entity = await _context.BrandStations
            .AsTracking()
            .FirstOrDefaultAsync(n => n.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove: a node is part of the Brand map other rows point at, so it is
        // history rather than garbage (#217). AsTracking matters - the context defaults to
        // NoTracking and the flag would not persist without it.
        entity.IsDeleted = true;
        entity.DeletedAtUtc = SystemClock.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
