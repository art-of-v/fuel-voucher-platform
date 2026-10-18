using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.DeletePackage;

public sealed class DeletePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    public DeletePackageCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeletePackageCommand command, CancellationToken ct = default)
    {
        var volume = await _context.FuelOfferVolumes
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (volume is null) return false;

        // Archive, never Remove: order_line_items point at packages, so removing one that was ever
        // bought against takes the record of what the customer ordered with it (#217). The
        // !IsDeleted filter keeps it out of the catalog and out of the buy flow.
        volume.IsDeleted = true;
        volume.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
