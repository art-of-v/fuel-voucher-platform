using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.DeletePackage;

public sealed class DeletePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    public DeletePackageCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeletePackageCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelPackages
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove: order_line_items point at packages, so removing one that was ever
        // bought against takes the record of what the customer ordered with it (#217). The
        // !IsDeleted filter keeps it out of the catalog and out of the buy flow.
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
