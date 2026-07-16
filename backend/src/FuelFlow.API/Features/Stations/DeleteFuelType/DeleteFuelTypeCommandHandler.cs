using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.DeleteFuelType;

public sealed class DeleteFuelTypeCommandHandler
{
    private readonly ApplicationDbContext _context;
    public DeleteFuelTypeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteFuelTypeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelTypes.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        _context.FuelTypes.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
