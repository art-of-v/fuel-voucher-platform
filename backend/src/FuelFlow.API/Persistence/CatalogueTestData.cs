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
    /// <summary>Find-or-create the fuel and apply the per-litre knobs to it.
    /// </summary>
    /// <remarks>
    /// Named for what it used to do - create the offer - because renaming it would touch roughly
    /// forty call sites in one commit for no gain. What it returns is the fuel, which is where every
    /// price it writes now lives.
    /// <para>
    /// Split from <see cref="SeedPackage"/> because a test fixture that builds its sizes as local
    /// entities and hands them to <c>AddRange</c> needs the fuel seeded without the nominal being
    /// added for it.
    /// </para>
    /// <para>
    /// <c>finalPricePerLiter</c> is a check rather than a setting, since 2026-10-17 — see the throw at
    /// the bottom of this method.
    /// </para>
    /// </remarks>
    public static FuelTypeEntity SeedOffer(
        this ApplicationDbContext context,
        Guid BrandId,
        string fuelTypeId,
        bool isTestData = false,
        decimal? supplierPricePerLiter = null,
        decimal? marginUahPerLiter = null,
        decimal? marginPercent = null,
        decimal? finalPricePerLiter = null,
        decimal? pumpPricePerLiter = null,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null)
    {
        var pool = DataKinds.ForPool(isTestData);
        var now = DateTime.UtcNow;
        // The fuel's id is a uuid, but a fixture still says which fuel it means by the name it has
        // always used. The mapping is the same one the migration used, so a fixture that means
        // "okko-95" means the row the production database actually holds.
        var fuelId = FuelIdMapper.ToUuid(fuelTypeId);
        // The fuel is created if the fixture has not seeded one, because a fixture that states a price
        // has to be able to state what sells it - and a half-configured fuel (a margin with no name, or
        // a name with no margin) is a test that passes for a reason nobody wrote down.
        var fuel = context.FuelTypes.Local.FirstOrDefault(f => f.Id == fuelId);
        if (fuel is null)
        {
            fuel = context.FuelTypes.FirstOrDefault(f => f.Id == fuelId);
            if (fuel is null)
            {
                fuel = new FuelTypeEntity
                {
                    Id = fuelId,
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

        // Every pricing figure is the fuel's. A fixture that left any of them on a row production
        // no longer reads would price every assertion off nothing - the price would look set and the
        // shelf would show nothing.
        if (supplierPricePerLiter is not null) fuel.SupplierPricePerLiter = supplierPricePerLiter;
        if (marginPercent is not null) fuel.MarginPercent = marginPercent;
        if (pumpPricePerLiter is not null) fuel.PumpPricePerLiter = pumpPricePerLiter;

        // `finalPricePerLiter` no longer configures anything — nothing stores the derived
        // figure — so it is checked against the formula instead of being written. Every call site
        // that passes it keeps working, and a fixture that asserted a price its own knobs do not
        // produce now fails here, at the fixture, with the two numbers in the message, rather than
        // three assertions later as an unexplained price on the storefront.
        if (finalPricePerLiter is not null)
        {
var computed = FuelPricing.FinalPerLiterOrNull(
            fuel.SupplierPricePerLiter,
            margin);
        if (computed != finalPricePerLiter)
        {
            throw new InvalidOperationException(
                $"SeedOffer({fuelTypeId}): finalPricePerLiter {finalPricePerLiter} does not match what the "
                + $"knobs produce ({computed?.ToString() ?? "null - no cost or no margin"}). Pass cost and "
                + "margin that yield the price the test needs.");
        }
        }

        return fuel;
    }

    /// <summary>Seed one sellable size for a pool: the nominal, priced off the fuel.</summary>
    /// </summary>
    /// <remarks>
    /// The fuel is found rather than created blindly because two sizes in one pool are two NOMINALS
    /// of one fuel. That is what the uniqueness constraint demands, and seeding them as two fuels
    /// would build a database state the application can never produce - a test that passes against it
    /// proves nothing.
    /// The knobs are written on the offer only when it is created, or when the caller passes them
    /// explicitly. Passing them for every volume of an offer would just re-create the duplication the
    /// split removed, and would make the last volume's price win - which is precisely the bug
    /// <c>ImportOkkoPumpPrices</c> used to have.
    /// </para>
    /// </remarks>
    public static FuelNominal SeedPackage(
        this ApplicationDbContext context,
        string id,
        Guid BrandId,
        string fuelTypeId,
        decimal liters,
        decimal price,
        bool isTestData = false,
        decimal? supplierPricePerLiter = null,
        decimal? marginUahPerLiter = null,
        decimal? marginPercent = null,
        decimal? finalPricePerLiter = null,
        decimal? pumpPricePerLiter = null,
        DateTime? createdAtUtc = null,
        DateTime? updatedAtUtc = null)
    {
        var pool = DataKinds.ForPool(isTestData);
        var fuelId = FuelIdMapper.ToUuid(fuelTypeId);

        context.SeedOffer(
            BrandId, fuelTypeId, isTestData,
            supplierPricePerLiter, marginUahPerLiter, marginPercent,
            finalPricePerLiter, pumpPricePerLiter,
            createdAtUtc, updatedAtUtc);

        // `price` used to be written as the per-package override, so it was the one number a fixture
        // had to state. There is no override any more: the charge is always cost + margin, capped by
        // the ceiling, so a fixture that wants a package to sell for something must say what the
        // fuel COSTS and what its MARGIN is. `price` is kept only to name what the test is about,
        // and is not written anywhere - a package seeded without knobs is unpriced, which the
        // storefront skips and checkout refuses, exactly as it would in production.
        _ = price;

        var nominal = new FuelNominal
        {
            FuelTypeId = fuelId,
            // The pool the fixture asked for: a rehearsal size is invisible to the live shelf, and
            // a live size is not a rehearsal one. The caller says which by isTestData.
            DataKind = DataKinds.ForPool(isTestData),
            Liters = liters,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow,
        };

        // `HasData` seeds the same (fuel, size) pairs into a fresh database, so adding one blindly
        // collides on the primary key. The size is a fact about the fuel, not about this fixture, so
        // finding it already there is the correct answer rather than something to fail over.
        var alreadyListed =
            context.FuelNominals.Local.Any(n => n.FuelTypeId == fuelId && n.DataKind == pool && n.Liters == liters)
            || context.FuelNominals.Any(n => n.FuelTypeId == fuelId && n.DataKind == pool && n.Liters == liters);
        if (alreadyListed)
            return context.FuelNominals.Local.FirstOrDefault(n => n.FuelTypeId == fuelId && n.Liters == liters)
                ?? new FuelNominal { FuelTypeId = fuelId, DataKind = pool, Liters = liters };

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
        Guid BrandId,
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
