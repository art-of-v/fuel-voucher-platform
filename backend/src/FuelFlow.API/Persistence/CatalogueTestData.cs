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
    /// Find-or-create the offer for <c>(Brand, fuel, pool)</c> and apply the per-litre knobs to it.
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
        string BrandId,
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
                BrandId = BrandId,
                FuelTypeId = fuelTypeId,
                DataKind = pool,
                CreatedAtUtc = createdAtUtc ?? now,
                UpdatedAtUtc = updatedAtUtc ?? now,
            };
            context.FuelOffers.Add(offer);
        }

        if (supplierPricePerLiter is not null) offer.SupplierPricePerLiter = supplierPricePerLiter;
        // The margin is the fuel's. The parameter keeps its name and its call sites: what changed is
        // where it lands, and the two derived figures below read it from the same place the production
        // code does. A fixture that set it on the offer would now price every assertion at cost.
        //
        // The fuel is created if the fixture has not seeded one, because a fixture that states a price
        // has to be able to state what sells it - and a half-configured fuel (a margin with no name, or
        // a name with no margin) is a test that passes for a reason nobody wrote down.
        var fuel = context.FuelTypes.Local.FirstOrDefault(f => f.Id == fuelTypeId);
        if (fuel is null)
        {
            fuel = context.FuelTypes.FirstOrDefault(f => f.Id == fuelTypeId);
            if (fuel is null)
            {
                fuel = new FuelTypeEntity
                {
                    Id = fuelTypeId,
                    Name = fuelTypeId,
                    BrandId = BrandId,
                    CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
                    UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow
                };
                context.FuelTypes.Add(fuel);
            }
            else
            {
                // Read out of the store rather than the tracker - the fixture cleared its tracker, which
                // is what `ChangeTracker.Clear` is for. Attaching it is what makes the margin below
                // survive to the database; an untracked entity takes the change and drops it on save.
                context.FuelTypes.Attach(fuel);
            }
        }

        if (marginUahPerLiter is not null) fuel.MarginUahPerLiter = marginUahPerLiter;
        var margin = fuel.MarginUahPerLiter;
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
            var computed = FuelPricing.FinalPerLiterOrNull(
                offer.SupplierPricePerLiter,
                margin,
                offer.PumpPricePerLiter,
                offer.MinDiscountPerLiter);
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
    /// <c>(Brand, fuel, pool)</c> and attach a volume carrying the package's id and frozen total.
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
    public static FuelNominal SeedPackage(
        this ApplicationDbContext context,
        string id,
        string BrandId,
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
    BrandId, fuelTypeId, isTestData,
    supplierPricePerLiter, marginUahPerLiter, marginPercent,
    finalPricePerLiter, pumpPricePerLiter, minDiscountPerLiter,
    createdAtUtc, updatedAtUtc);

// `price` used to be written as the per-package override, so it was the one number a fixture had
    // to state. There is no override any more: the charge is always cost + margin, capped by the
    // ceiling, so a fixture that wants a package to sell for something must say what the fuel COSTS
    // and what its MARGIN is. `price` is kept only to name what the test is about, and is not
    // written anywhere — a package seeded without knobs is unpriced, which the storefront skips and
    // checkout refuses, exactly as it would in production.
    _ = price;

        var nominal = new FuelNominal
        {
            FuelTypeId = fuelTypeId,
            // The pool the fixture asked for: a rehearsal size is invisible to the live shelf, and
            // a live size is not a rehearsal one. The caller says which by isTestData.
            DataKind = offer.DataKind,
            Liters = liters,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow,
        };

        // `HasData` seeds the same (fuel, size) pairs into a fresh database, so adding one blindly
        // collides on the primary key. The size is a fact about the fuel, not about this fixture, so
        // finding it already there is the correct answer rather than something to fail over.
        var alreadyListed =
            context.FuelNominals.Local.Any(n => n.FuelTypeId == fuelTypeId && n.DataKind == offer.DataKind && n.Liters == liters)
            || context.FuelNominals.Any(n => n.FuelTypeId == fuelTypeId && n.DataKind == offer.DataKind && n.Liters == liters);
        if (alreadyListed)
            return context.FuelNominals.Local.FirstOrDefault(n => n.FuelTypeId == fuelTypeId && n.Liters == liters)
                ?? new FuelNominal { FuelTypeId = fuelTypeId, Liters = liters };

        context.FuelNominals.Add(nominal);
        return nominal;
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
        string BrandId,
        string fuelTypeId,
        decimal liters,
        decimal realPrice,
        decimal qaPrice,
        decimal? supplierPricePerLiter = null,
        decimal? marginUahPerLiter = null,
        decimal? finalPricePerLiter = null)
    {
        context.SeedPackage(
            $"{idPrefix}-real", BrandId, fuelTypeId, liters, realPrice,
            isTestData: false, supplierPricePerLiter: supplierPricePerLiter,
            marginUahPerLiter: marginUahPerLiter,
            finalPricePerLiter: finalPricePerLiter ?? realPrice / liters);
        context.SeedPackage(
            $"{idPrefix}-qa", BrandId, fuelTypeId, liters, qaPrice,
            isTestData: true, supplierPricePerLiter: supplierPricePerLiter,
            marginUahPerLiter: marginUahPerLiter,
            finalPricePerLiter: finalPricePerLiter ?? qaPrice / liters);
    }
}
