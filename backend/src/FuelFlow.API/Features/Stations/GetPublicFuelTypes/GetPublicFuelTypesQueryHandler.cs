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
            .Select(f => new PublicFuelTypeResponse(
                f.Id,
                f.BrandId,
                slugs.TryGetValue(f.BrandId, out var slug) ? slug : string.Empty,
                f.Name,
                f.BasePrice,
                f.DiscountPrice,
                f.PumpPricePerLiter,
                FuelPricing.FinalPerLiterOrNull(
                    f.SupplierPricePerLiter, f.MarginUahPerLiter, f.PumpPricePerLiter, f.MinDiscountPerLiter),
                available.Contains(f.Id)))
            .ToList();
    }
}
