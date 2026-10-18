using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.DeleteFuelType;

public sealed class DeleteFuelTypeCommandHandler
{
    private readonly ApplicationDbContext _context;
    public DeleteFuelTypeCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteFuelTypeCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelTypes
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove. fuel_vouchers.fuel_type_id CASCADEs from here, so deleting a fuel
        // type used to destroy every voucher sold against it - paid-for paper, and the only record
        // of what the customer bought. That is the history loss #217 exists to stop.
        //
        // AsTracking is load-bearing: the context default is TrackAll, so setting the flag on an
        // untracked entity persists nothing and this would report success having changed nothing.
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
