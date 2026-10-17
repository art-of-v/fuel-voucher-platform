using FuelFlow.SharedKernel.Domain;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Stations.UpdatePackage;

/// <summary>
/// The only endpoint that can set a per-nominal price override, and deliberately the only one.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this endpoint is for.</b> A volume's price is its offer's rate × its litres. Occasionally an
/// operator wants one size to carry a round marketing price — 50 L for 500 rather than 489.30 — and
/// that is a real thing to want. This is where it is expressed, as an explicit override on one nominal,
/// and nowhere else: every other write path leaves it alone.
/// </para>
/// <para>
/// <b>Use it sparingly, and the schema now says so.</b> Before this, setting a package price wrote a
/// column that five other paths then overwrote back to the arithmetic, so an operator's deliberate price
/// survived only until the next pump import. An override is not that: it survives repricing, which means
/// it also silently opts that one size out of every future price change. A fuel that should follow the
/// market has no override, and the bulk way to price a fuel is <c>PUT .../fuels/{id}</c>, which moves all
/// its nominals at once.
/// </para>
/// <para>
/// <b>It cannot price a fuel under cost.</b> The guard is not applied here — this endpoint sets a
/// catalogue row, and the block lives where money is charged — but the override it writes is judged by
/// the same below-cost check at checkout, against the price that will actually be charged. Setting one
/// does not make a loss-making sale possible; it makes the whole fuel look loss-making.
/// </para>
/// </remarks>
public sealed class UpdatePackageCommandHandler
{
    private readonly ApplicationDbContext _context;
    public UpdatePackageCommandHandler(ApplicationDbContext context) => _context = context;

    public async Task<bool> HandleAsync(UpdatePackageCommand command, CancellationToken ct = default)
    {
        var volume = await _context.FuelOfferVolumes
            .FirstOrDefaultAsync(x => x.Id == command.Id, ct);
        if (volume is null) return false;

        // Only the two facts a package owns are writable here: how much it holds, and whether this
        // nominal has a price of its own. Station and fuel are the OFFER's identity - UNIQUE
        // (station_id, fuel_type_id, data_kind) - so moving a package to another station or fuel is not an
        // update, it is moving it to another offer, and it would drag the rest of that offer's volumes
        // with it if done here. Re-keying a package is what the brand screen does; this endpoint cannot
        // do it and silently ignoring the field is better than corrupting the offer it belongs to.
        //
        // fuel_name is dropped for the same reason it is gone from the table: it is a copy of
        // fuel_types.name, and this was the handler that had to keep that copy in step.
        volume.Liters = command.Updated.Liters;
        // Null clears the override and puts the nominal back on the offer's rate. That is the direction
        // an operator should reach for when they are done with a special price, and it is the reason the
        // field is nullable rather than "0 means free".
        volume.PriceOverrideUah = command.Updated.PriceOverrideUah;
        _context.FuelOfferVolumes.Update(volume);

        await _context.SaveChangesAsync(ct);
        return true;
    }
}