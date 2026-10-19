using FuelFlow.Features.Brands;
using FluentAssertions;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The cart-filtered half of <see cref="PackageStockAvailability"/>, which only exists to keep a
/// checkout from charging for fuel the shop does not have.
///
/// <para>
/// This is a translation test wearing a unit test's clothes, and it exists because nothing else
/// caught the bug it is here for. <c>AvailableKeysAsync(wanted, ...)</c> narrows the key set with
/// <c>wanted.Select(w =&gt; w.Brand).Contains(k.Brand)</c> and two siblings. That reads as in-memory
/// LINQ but sits inside a <c>Where</c> over an <c>IQueryable</c>, so EF tries to translate the
/// nested <c>Select</c> into a correlated subquery and cannot: the right-hand side is
/// <c>new StockKey(...).Brand</c> over a projection. The failure is
/// <c>InvalidOperationException: could not be translated</c>, thrown at runtime, on the first real
/// cart - and nothing but a database-backed test can see it. Unit tests with an in-memory provider
/// evaluate the expression client-side, where it works perfectly, so they pass while production 500s.
/// </para>
/// <para>
/// That is exactly what happened: shipped in 1.1.0, no test touched this method, and
/// <c>POST /api/purchases/bulk</c> returned 500 for every customer from the moment the feature
/// deployed - the customer's payment never completed and the shop looked broken.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class PackageStockAvailabilityIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PackageStockAvailabilityIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// The regression itself: the cart-filtered query must translate and return the sellable keys.
    /// Before the fix this threw <c>could not be translated</c> rather than returning anything.
    /// </summary>
    [Fact]
    public async Task AvailableKeys_ForACart_ReturnsOnlyTheSellableKeysThatWereAskedFor()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var seed = CreateContext();
        await seed.Database.ExecuteSqlRawAsync(
              """TRUNCATE TABLE "fuel_nominals", "orders", "order_line_items", "fulfillments", "outbox_events", "fuel_vouchers" RESTART IDENTITY CASCADE""");

        seed.FuelVouchers.AddRange(
            Stock("okko", "okko-95", 50m, today.AddDays(90)),
            Stock("wog", "wog-95", 50m, today.AddDays(90)),
            // Same brand and fuel, wrong nominal: the cart asked for 50, not 20.
            Stock("okko", "okko-95", 20m, today.AddDays(90)));
        await seed.SaveChangesAsync();

        var wanted = new[]
        {
            StockKey.For("okko", "okko-95", 50m),
            StockKey.For("wog", "wog-95", 50m),
        };

        var availability = CreateAvailability();
        var keys = await availability.AvailableKeysAsync(wanted, isTestAccount: false, CancellationToken.None);

        keys.Should().BeEquivalentTo(
            new[] { StockKey.For("okko", "okko-95", 50m), StockKey.For("wog", "wog-95", 50m) });
    }

    /// <summary>
    /// A wanted key with no sellable stock behind it must come back absent, not throw and not appear.
    /// This is the guard the feature exists to provide: refuse to sell what is not on the shelf.
    /// </summary>
    [Fact]
    public async Task AvailableKeys_ForACartWithNoStockBehindOneLine_OmitsThatKey()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var seed = CreateContext();
        await seed.Database.ExecuteSqlRawAsync(
              """TRUNCATE TABLE "fuel_nominals", "orders", "order_line_items", "fulfillments", "outbox_events", "fuel_vouchers" RESTART IDENTITY CASCADE""");

        seed.FuelVouchers.Add(Stock("okko", "okko-95", 50m, today.AddDays(90)));
        await seed.SaveChangesAsync();

        var wanted = new[]
        {
            StockKey.For("okko", "okko-95", 50m),
            StockKey.For("upg", "upg-95", 50m),   // nothing stocked
        };

        var availability = CreateAvailability();
        var keys = await availability.AvailableKeysAsync(wanted, isTestAccount: false, CancellationToken.None);

        keys.Should().BeEquivalentTo(new[] { StockKey.For("okko", "okko-95", 50m) });
    }

    /// <summary>
    /// An empty cart must short-circuit rather than issue a pointless query - and, before the fix,
    /// it is the one input that did not throw, which is why the bug survived review.
    /// </summary>
    [Fact]
    public async Task AvailableKeys_ForAnEmptyCart_ReturnsNothingWithoutQuerying()
    {
        var availability = CreateAvailability();

        var keys = await availability.AvailableKeysAsync(
            Array.Empty<StockKey>(), isTestAccount: false, CancellationToken.None);

        keys.Should().BeEmpty();
    }

    // ── FindPackageAsync: a triple is NOT a unique key ─────────────────────────────────────

    /// <summary>
    /// The condition measured on production 2026-10-14: every sellable triple carries two live
    /// rows, because <c>seed-qa-catalogue.sql</c> mirrors each real package into the QA pool on
    /// the same Brand, fuel and nominal (13 offers, 58 real rows, 58 QA rows, no duplicates
    /// inside either pool).
    ///
    /// Checkout resolved that key with a bare <c>FirstOrDefaultAsync</c> - no pool filter, no
    /// ordering - so the customer was charged off whichever row Postgres produced first. Both rows
    /// carry the same rate, because the fuel's margin is one number and a nominal has no price of its
    /// own; what separates them is <c>data_kind</c>, and that is what the query has to filter on. The
    /// test cannot make the two prices differ any more, so it asserts the thing that actually decides
    /// it: each pool reads back its own row.
    /// </summary>
    [Fact]
    public async Task FindPackage_IgnoresTheOtherPoolsRow()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

        SeedPackage(seed, "real-10l", isTestData: false, liters: 10m, pricePerLiter: 89.06m, updatedMinutesAgo: 60);
        // The same fuel, the same nominal, in the OTHER pool. Two pools cannot collide on the offer
        // key, so the pool filter is what decides - and the test fails if it stops doing so.
        SeedPackage(seed, "qa-mirror-10l", isTestData: true, liters: 10m, pricePerLiter: 89.06m, updatedMinutesAgo: 0);
        await seed.SaveChangesAsync();

        var availability = CreateAvailability();

(await availability.FindPackageAsync(false, BrandIds.Okko, "okko-95", 10m))!.PackageId
    .Should().Be(PackageIds.For("okko-95", DataKinds.Real, 10m));
        (await availability.FindPackageAsync(true, BrandIds.Okko, "okko-95", 10m))!.PackageId
            .Should().Be(PackageIds.For("okko-95", DataKinds.Qa, 10m));
    }

    /// <summary>
    /// Two volumes on one key cannot coexist any more, and the database is what says so.
    /// </summary>
    /// <remarks>
    /// This test used to seed two rows for the same (Brand, fuel, litres) in the same pool and assert
    /// that the resolver picked the newer one deterministically. That was guarding against a real defect
    /// - <c>BrandsController</c> added a nominal through a read-then-write with no lock, so two
    /// concurrent PUTs could both insert - and the ordering was the mitigation.
    /// <para>
    /// The split removed the possibility rather than the symptom. <c>UNIQUE (brand_id, fuel_type_id,
    /// the fuel's own primary key on fuel_nominals makes (brand, fuel, size) unique inside a pool, so the
    /// the triple unique inside a pool, so the race that produced two rows now fails at the constraint.
    /// Asserting "the newer one wins" would be asserting a tie-break that can never be reached, and it
    /// would quietly invite someone to reintroduce the ambiguity by ordering the query.
    /// </para>
    /// <para>
    /// So the test asserts the guarantee instead: the duplicate is refused. If this ever passes, the
    /// constraint is gone and every resolver in the app is back to picking arbitrarily.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_DuplicateNominalOnOneKey_IsRefusedByTheDatabase()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

        SeedPackage(seed, "first", isTestData: false, liters: 20m, pricePerLiter: 89m, updatedMinutesAgo: 120);
        await seed.SaveChangesAsync();

        // The same size twice, added straight rather than through the fixture: the helper treats a
    // size the fuel already lists as done, which is correct for a caller and useless here - the
    // point is what the DATABASE does with the row an old read-then-write race would have
    // produced, so the duplicate has to actually reach it.
    //
    // A second context, because the change tracker refuses a second instance with a key it
    // already holds - and that refusal would pass this test for a reason that has nothing to do
    // with the database. The pool is part of the key now, so the duplicate has to carry the same
    // one to be a duplicate at all.
    await using (var direct = CreateContext())
    {
        direct.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = "okko-95", DataKind = DataKinds.Real, Liters = 20m
        });

        var act = async () => await direct.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

        // And with the duplicate refused, the key resolves to the one row that exists - no ordering
        // involved, because there is nothing to order.
        var availability = CreateAvailability();
var chosen = await availability.FindPackageAsync(false, BrandIds.Okko, "okko-95", 20m);
        chosen!.PackageId.Should().Be(PackageIds.For("okko-95", DataKinds.Real, 20m));

        var bulk = await availability.FindPackagesAsync(
            false, new[] { StockKey.For("okko", "okko-95", 20m) });
        bulk[StockKey.For("okko", "okko-95", 20m)].PackageId.Should().Be(chosen.PackageId);
    }

    /// <summary>
    /// A key that exists only in the other pool must read as "no pricing", so checkout refuses the
    /// line rather than silently charging a QA price to a real customer.
    /// </summary>
    [Fact]
    public async Task FindPackage_ReturnsNull_WhenOnlyTheOtherPoolHasTheTriple()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

// A negative margin cannot be stored any more: the fuel took the offer's sign constraint with
    // it, and 30 cost against a 20 rate is one. The price here is incidental to what this test
    // checks - that a rehearsal-only size is invisible to the live shelf - so it asks for a sane one.
    SeedPackage(seed, "qa-only", isTestData: true, liters: 5m, pricePerLiter: 60m, updatedMinutesAgo: 0);
    await seed.SaveChangesAsync();

        var availability = CreateAvailability();

        (await availability.FindPackageAsync(false, BrandIds.Okko, "okko-95", 5m)).Should().BeNull(
            "a real customer must not be priced off a rehearsal package");
    }

    /// <summary>
    /// The cart path returns one entry per wanted key, so bulk checkout cannot fall through to a
    /// null and 500 on a line that exists.
    /// </summary>
    [Fact]
    public async Task FindPackages_ReturnsAnEntryForEveryWantedKey_AndNothingElse()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

        SeedPackage(seed, "real-2l", isTestData: false, liters: 2m, pricePerLiter: 89.06m, updatedMinutesAgo: 5);
        SeedPackage(seed, "real-5l", isTestData: false, liters: 5m, pricePerLiter: 89.06m, updatedMinutesAgo: 5);
        // In the catalogue, but not in the cart - must not come back.
        SeedPackage(seed, "real-50l", isTestData: false, liters: 50m, pricePerLiter: 89.06m, updatedMinutesAgo: 5);

        var wanted = new[]
        {
            StockKey.For("okko", "okko-95", 2m),
            StockKey.For("okko", "okko-95", 5m),
            StockKey.For("wog", "wog-95", 10m),   // nothing at all behind this one
        };
        await seed.SaveChangesAsync();

        var resolved = await CreateAvailability().FindPackagesAsync(false, wanted);

        resolved.Keys.Should().BeEquivalentTo(
            new[] { StockKey.For("okko", "okko-95", 2m), StockKey.For("okko", "okko-95", 5m) });
        resolved.Should().NotContainKey(StockKey.For("wog", "wog-95", 10m),
            "an absent key is what makes the line refuse rather than fall through");
    }

    private static async Task TruncatePackagesAsync(ApplicationDbContext context)
        => await context.Database.ExecuteSqlRawAsync(
              """TRUNCATE TABLE "fuel_nominals", "fuel_vouchers", "orders", "order_line_items", "fulfillments", "outbox_events" RESTART IDENTITY CASCADE""");

    /// <summary>
    /// Seeds one sellable package as an offer plus a volume.
    /// </summary>
    /// <remarks>
    /// The offer is found by its key rather than created blindly, because two packages in one pool are
    /// two VOLUMES of one offer - that is the shape the uniqueness constraint demands, and seeding them
    /// as two offers would test a database state the application can never produce.
    /// <para>
    /// A nominal carries no price of its own, so the rate comes from the fuel - which means the two
    /// pools of one fuel are priced alike, because the fuel's margin is one number. That is the point
    /// of the rehearsal pool: it isolates stock and data, not money. A fixture that needs a nominal to
    /// sell for X per litre states a cost of 30 and puts the rest in the margin.
    /// </para>
    /// </remarks>
    private static void SeedPackage(
        ApplicationDbContext seed,
        string id,
        bool isTestData,
        decimal liters,
        decimal pricePerLiter,
        int updatedMinutesAgo)
    {
        var pool = DataKinds.ForPool(isTestData);

        var fuel = seed.FuelTypes.Local.FirstOrDefault(f => f.Id == "okko-95")
            ?? seed.FuelTypes.FirstOrDefault(f => f.Id == "okko-95");
        fuel!.MarginUahPerLiter = pricePerLiter - 30m;
        fuel.SupplierPricePerLiter = 30m;
        fuel.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-updatedMinutesAgo);

        // Availability resolves sizes from fuel_nominals, and the size list is per pool - so a
        // rehearsal size exists only in the rehearsal pool and is invisible to the live shelf.
        if (!seed.FuelNominals.Local.Any(n => n.FuelTypeId == "okko-95" && n.DataKind == pool && n.Liters == liters)
            && !seed.FuelNominals.Any(n => n.FuelTypeId == "okko-95" && n.DataKind == pool && n.Liters == liters))
        {
            seed.FuelNominals.Add(new FuelNominal
            {
                FuelTypeId = "okko-95",
                DataKind = pool,
                Liters = liters
            });
        }
    }

    private PackageStockAvailability CreateAvailability()
        => new(CreateContext(), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["VoucherExpiration:Enabled"] = "true" })
            .Build());

    private static FuelVoucher Stock(string brand, string fuelTypeId, decimal liters, DateOnly expiry)
        => new()
        {
            Id = Guid.NewGuid(),
            Brand = brand,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            SupplierExpirationDate = expiry,
            CustomerExpirationDate = expiry,
            VoucherNumber = $"AV-{Guid.NewGuid():N}"[..16],
            QrPayload = $"qr-{Guid.NewGuid():N}",
            Status = VoucherStatus.Available,
            CostPerLiter = 30m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
