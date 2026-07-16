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
        var entity = await _context.Stations.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        _context.Stations.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
