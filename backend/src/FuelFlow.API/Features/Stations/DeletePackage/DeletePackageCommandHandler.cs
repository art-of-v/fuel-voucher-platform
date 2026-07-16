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
        var entity = await _context.FuelPackages.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        _context.FuelPackages.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
