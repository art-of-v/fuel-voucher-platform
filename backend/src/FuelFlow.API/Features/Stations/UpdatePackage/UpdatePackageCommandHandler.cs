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
        var volume = await _context.FuelOfferVolumes
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (volume is null) return false;

        // Only the two facts a package owns are writable here: how much it holds, and what it costs as
        // a package. Station and fuel are the OFFER's identity - UNIQUE (station_id, fuel_type_id,
        // data_kind) - so moving a package to another station or fuel is not an update, it is moving it
        // to another offer, and it would drag the rest of that offer's volumes with it if done here.
        // Re-keying a package is what the brand screen does; this endpoint cannot do it and silently
        // ignoring the field is better than corrupting the offer it belongs to.
        //
        // fuel_name is dropped for the same reason it is gone from the table: it is a copy of
        // fuel_types.name, and this was the handler that had to keep that copy in step.
        volume.Liters = command.Updated.Liters;
        volume.PackagePrice = command.Updated.Price;
        _context.FuelOfferVolumes.Update(volume);

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
