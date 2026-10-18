using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.DeleteBrand;

public sealed class DeleteBrandCommandHandler
{
    private readonly ApplicationDbContext _context;

    public DeleteBrandCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(DeleteBrandCommand command, CancellationToken ct = default)
    {
        var entity = await _context.Brands
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        // Archive, never Remove: contracts and Brand_nodes cascade from here, and those point
        // toward vouchers, so removing a Brand could reach paid-for paper (#217).
        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
