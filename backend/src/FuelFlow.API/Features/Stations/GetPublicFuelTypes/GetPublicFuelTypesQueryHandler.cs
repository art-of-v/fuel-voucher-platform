using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Features.Brands.GetPublicFuelTypes;

public sealed class GetPublicFuelTypesQueryHandler
{
    private readonly ApplicationDbContext _context;
    private readonly PackageStockAvailability _stock;

    public GetPublicFuelTypesQueryHandler(
        ApplicationDbContext context,
        PackageStockAvailability stock)
    {
        _context = context;
        _stock = stock;
    }

    /// <summary>
    /// Materialised and mapped rather than projected, because the slug is needed and
    /// <c>fuel_types</c> has no navigation to <c>brands</c>: <c>fuel_types.brand_id</c> carries no
    /// foreign key, so EF cannot join it. Adding one is a schema change beyond this slice, and the
    /// catalogue is a few hundred rows, so one dictionary beats one migration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three prices it reports are what a customer needs to be told, and which one of them is
    /// THE price depends on whether we can sell the fuel:
    /// </para>
    /// <list type="bullet">
    /// <item><description>the price we charge, when we have a margin - that is what a customer pays;</description></item>
    /// <item><description>the column price, when we cannot sell it yet - what the market charges, and the
    /// only honest number to show for a fuel nobody has costed.</description></item>
    /// </list>
    /// <para>
    /// The availability is the caller's own pool, because a QA account and a customer can legitimately
    /// see different answers for the same fuel.
    /// </para>
    /// </remarks>
    public async Task<List<PublicFuelTypeResponse>> HandleAsync(GetPublicFuelTypesQuery query, CancellationToken ct = default)
    {
        var fuels = await _context.FuelTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.BrandId,
                x.BasePrice,
                x.DiscountPrice,
                x.PumpPricePerLiter,
                x.SupplierPricePerLiter,
                x.MarginUahPerLiter,
                x.MinDiscountPerLiter,
            })
            .ToListAsync(ct);

        var slugs = await _context.Brands.AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Slug, ct);

        var isTestAccount = await _stock.IsTestAccountAsync(query.CallerUserId, ct);
        var available = await _stock.AvailableFuelIdsAsync(isTestAccount, ct);

        return fuels
            .Select(f =>
            {
                var isAvailable = available.Contains(f.Id);
                var ours = FuelPricing.FinalPerLiterOrNull(
                    f.SupplierPricePerLiter, f.MarginUahPerLiter, f.PumpPricePerLiter, f.MinDiscountPerLiter);

                // The price this fuel is shown at, decided ONCE here so that every client shows the
                // same number: ours when the shop can sell it, the column price when it cannot, and
                // the column price is also what a client that has not yet picked up the new fields
                // reads - because `discountPrice` IS that number here.
                //
                // The rule was the owner's, and it is a rule about what a customer is told: a fuel
                // nobody has costed must not read as free, and the market's price must not be
                // presented as ours.
                var shown = isAvailable
                    ? (ours ?? f.PumpPricePerLiter)
                    : (f.PumpPricePerLiter ?? ours);

                return new PublicFuelTypeResponse(
                    f.Id,
                    f.BrandId,
                    slugs.TryGetValue(f.BrandId, out var slug) ? slug : string.Empty,
                    f.Name,
                    f.BasePrice,
                    // The legacy field, and it now carries the price TO SHOW rather than a stale
                    // column: a client that has not picked up the new fields yet reads this one, and
                    // it must not read "0 UAH" for a fuel the market charges 92.90 for.
                    shown ?? 0m,
                    f.PumpPricePerLiter,
                    ours,
                    isAvailable);
            })
            .ToList();
    }
}
