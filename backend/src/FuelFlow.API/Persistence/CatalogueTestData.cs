using FuelFlow.SharedKernel.Domain;

namespace FuelFlow.Persistence;

/// <summary>
/// Seeds the catalogue as an offer plus volumes - the shape the application actually reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in the product assembly rather than in a test project.</b> There are three test
/// projects and no shared test library, so a helper placed in "the test project" means three copies
/// that drift. The assembly already grants <c>InternalsVisibleTo</c> to the test projects, so this is
/// reachable from them without becoming part of the public API.
/// </para>
///
/// <para>
/// <b>What it replaced.</b> Roughly twenty test files each built a <see cref="FuelPackage"/> literal by
/// hand. After the split that literal is not a row any more - it is a join - so leaving them would have
/// meant twenty files quietly seeding a table nothing reads, which fails in the most confusing way
/// possible: green tests that prove nothing about the catalogue.
/// </para>
/// </remarks>
internal static class CatalogueTestData
{
    /// <summary>
    /// Seed one sellable package: find-or-create the offer for
    /// <c>(station, fuel, pool)</c> and attach a volume carrying the package's id and frozen total.
    /// </summary>
    /// <remarks>
    /// The offer is found rather than created blindly because two packages in one pool are two VOLUMES
    /// of one offer. That is what the uniqueness constraint demands, and seeding them as two offers
    /// would build a database state the application can never produce - a test that passes against it
    /// proves nothing.
    /// <para>
    /// The knobs are written on the offer only when it is created, or when the caller passes them
    /// explicitly. Passing them for every volume of an offer would just re-create the duplication the
    /// split removed, and would make the last volume's price win - which is precisely the bug
    /// <c>ImportOkkoPumpPrices</c> used to have.
    /// </para>
    /// </remarks>
    public static FuelOfferVolume SeedPackage(
        this ApplicationDbContext context,
        string id,
        string stationId,
        string fuelTypeId,
        decimal liters,
        decimal price,
        bool isTestData = false,
        decimal? supplierPricePerLiter = null,
        decimal? marginUahPerLiter = null,
        decimal? marginPercent = null,
        decimal? finalPricePerLiter = null,
        decimal? pumpPricePerLiter = null,
        decimal? minDiscountPerLiter = null,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null)
    {
        var pool = DataKinds.ForPool(isTestData);
        var offerId = OfferKeys.For(stationId, fuelTypeId, pool);
        var now = DateTime.UtcNow;
        var created = createdAtUtc ?? now;
        var updated = updatedAtUtc ?? now;

        var offer = context.FuelOffers.Local.FirstOrDefault(o => o.Id == offerId)
                    ?? context.FuelOffers.FirstOrDefault(o => o.Id == offerId);

        if (offer is null)
        {
            offer = new FuelOffer
            {
                Id = offerId,
                StationId = stationId,
                FuelTypeId = fuelTypeId,
                DataKind = pool,
                CreatedAtUtc = created,
                UpdatedAtUtc = updated,
            };
            context.FuelOffers.Add(offer);
        }

        if (supplierPricePerLiter is not null) offer.SupplierPricePerLiter = supplierPricePerLiter;
        if (marginUahPerLiter is not null) offer.MarginUahPerLiter = marginUahPerLiter;
        if (marginPercent is not null) offer.MarginPercent = marginPercent;
        if (finalPricePerLiter is not null) offer.FinalPricePerLiter = finalPricePerLiter;
        if (pumpPricePerLiter is not null) offer.PumpPricePerLiter = pumpPricePerLiter;
        if (minDiscountPerLiter is not null) offer.MinDiscountPerLiter = minDiscountPerLiter;

        var volume = new FuelOfferVolume
        {
            Id = id,
            OfferId = offerId,
            Liters = liters,
            PackagePrice = price,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated,
        };
        context.FuelOfferVolumes.Add(volume);
        return volume;
    }

    /// <summary>
    /// Seed the same nominal at one fuel across both pools, with each pool priced differently.
    /// </summary>
    /// <remarks>
    /// For tests about pool isolation. The rehearsal price being deliberately absurd is the point: a
    /// test that passes while the QA price leaks into a live quote is the bug this shape exists to
    /// catch, and it cannot catch it if the two catalogues hold the same numbers.
    /// </remarks>
    public static void SeedPackageInBothPools(
        this ApplicationDbContext context,
        string idPrefix,
        string stationId,
        string fuelTypeId,
        decimal liters,
        decimal realPrice,
        decimal qaPrice,
        decimal? supplierPricePerLiter = null,
        decimal? marginUahPerLiter = null,
        decimal? finalPricePerLiter = null)
    {
        context.SeedPackage(
            $"{idPrefix}-real", stationId, fuelTypeId, liters, realPrice,
            isTestData: false, supplierPricePerLiter: supplierPricePerLiter,
            marginUahPerLiter: marginUahPerLiter,
            finalPricePerLiter: finalPricePerLiter ?? realPrice / liters);
        context.SeedPackage(
            $"{idPrefix}-qa", stationId, fuelTypeId, liters, qaPrice,
            isTestData: true, supplierPricePerLiter: supplierPricePerLiter,
            marginUahPerLiter: marginUahPerLiter,
            finalPricePerLiter: finalPricePerLiter ?? qaPrice / liters);
    }
}