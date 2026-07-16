using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.UpdatePackage;

public sealed class UpdatePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    public UpdatePackageCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdatePackageCommand command, CancellationToken ct = default)
    {
        var entity = await _context.FuelPackages.FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (entity is null) return false;

        entity.StationId = command.Updated.StationId;
        entity.FuelTypeId = command.Updated.FuelTypeId;
        entity.FuelName = command.Updated.FuelName;
        entity.Liters = command.Updated.Liters;
        entity.Price = command.Updated.Price;
        entity.OriginalPrice = command.Updated.OriginalPrice;

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
