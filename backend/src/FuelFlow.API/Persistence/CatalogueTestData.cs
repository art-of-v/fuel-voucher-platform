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
    /// Find-or-create the offer for <c>(station, fuel, pool)</c> and apply the per-litre knobs to it.
    /// </summary>
    /// <remarks>
    /// Split from <see cref="SeedPackage"/> because a test fixture that builds its volumes as local
    /// entities and hands them to <c>AddRange</c> needs the offer added without the volume being added
    /// for it. Returning the volume and letting the caller's <c>AddRange</c> do the adding keeps every
    /// such call site untouched - which matters, because rewriting a dozen <c>AddRange</c> blocks by
    /// hand is how fixtures quietly lose an argument.
    /// <para>
    /// <c>finalPricePerLiter</c> is a check rather than a setting, since 2026-10-17 — see the throw at
    /// the bottom of this method.
    /// </para>
    /// </remarks>
    public static FuelOffer SeedOffer(
        this ApplicationDbContext context,
        string stationId,
        string fuelTypeId,
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
        var offerId = OfferKeys.For(fuelTypeId, pool);
        var now = DateTime.UtcNow;

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
                CreatedAtUtc = createdAtUtc ?? now,
                UpdatedAtUtc = updatedAtUtc ?? now,
            };
            context.FuelOffers.Add(offer);
        }

        if (supplierPricePerLiter is not null) offer.SupplierPricePerLiter = supplierPricePerLiter;
        if (marginUahPerLiter is not null) offer.MarginUahPerLiter = marginUahPerLiter;
        if (marginPercent is not null) offer.MarginPercent = marginPercent;
        if (pumpPricePerLiter is not null) offer.PumpPricePerLiter = pumpPricePerLiter;
        if (minDiscountPerLiter is not null) offer.MinDiscountPerLiter = minDiscountPerLiter;

        // `finalPricePerLiter` no longer configures anything — the offer stopped storing the derived
        // figure on 2026-10-17 — so it is checked against the formula instead of being written. Every
        // call site that passes it keeps working, and a fixture that asserted a price its own knobs do
        // not produce now fails here, at the fixture, with the two numbers in the message, rather than
        // three assertions later as an unexplained price on the storefront.
        if (finalPricePerLiter is not null)
        {
            var computed = offer.ComputedFinalPricePerLiter;
            if (computed != finalPricePerLiter)
            {
                throw new InvalidOperationException(
                    $"SeedOffer({offer.Id}): finalPricePerLiter {finalPricePerLiter} does not match what the "
                    + $"knobs produce ({computed?.ToString() ?? "null - no cost or no margin"}). Pass cost and "
                    + "margin that yield the price the test needs.");
            }
        }

        return offer;
    }

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
var offer = context.SeedOffer(
            stationId, fuelTypeId, isTestData,
            supplierPricePerLiter, marginUahPerLiter, marginPercent,
            finalPricePerLiter, pumpPricePerLiter, minDiscountPerLiter,
            createdAtUtc, updatedAtUtc);

        // `price` is what the test wants this package to sell for. Under the override model that is
        // either the offer's own rate × litres — in which case it is written as nothing at all and the
        // package follows its fuel — or a deliberate exception, in which case it becomes the override and
        // stays visible as one. Which of the two it is is decided here rather than by each test, so a
        // fixture cannot accidentally pin a price it meant to be derived.
        var derived = FuelPricing.TryPackagePrice(
            offer.SupplierPricePerLiter,
            offer.MarginUahPerLiter,
            offer.PumpPricePerLiter,
            offer.MinDiscountPerLiter,
            liters,
            overrideUah: null);

        var volume = new FuelOfferVolume
        {
            Id = id,
            OfferId = offer.Id,
            Liters = liters,
            PriceOverrideUah = derived == price ? null : price,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow,
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