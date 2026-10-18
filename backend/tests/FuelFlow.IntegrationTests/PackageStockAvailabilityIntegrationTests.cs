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
            """TRUNCATE TABLE "orders", "order_line_items", "fulfillments", "outbox_events", "fuel_vouchers" RESTART IDENTITY CASCADE""");

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
            """TRUNCATE TABLE "orders", "order_line_items", "fulfillments", "outbox_events", "fuel_vouchers" RESTART IDENTITY CASCADE""");

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
    /// the same station, fuel and nominal (13 offers, 58 real rows, 58 QA rows, no duplicates
    /// inside either pool).
    ///
    /// Checkout resolved that key with a bare <c>FirstOrDefaultAsync</c> - no pool filter, no
    /// ordering - so the customer was charged off whichever row Postgres produced first. The two
    /// rows carry the same price today only because the seed copies it, which is luck rather than
    /// a property. Here the QA row is priced differently on purpose, so the old code fails and the
    /// new code cannot.
    /// </summary>
    [Fact]
    public async Task FindPackage_IgnoresTheOtherPoolsRow_EvenWhenItsPriceDiffers()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

        SeedPackage(seed, "real-10l", isTestData: false, liters: 10m, price: 890.60m, updatedMinutesAgo: 60);
        // Deliberately a different price, in the OTHER pool. Two pools cannot collide on the offer key,
        // so the pool filter is what decides - and the test fails if it stops doing so.
        SeedPackage(seed, "qa-mirror-10l", isTestData: true, liters: 10m, price: 1m, updatedMinutesAgo: 0);
        await seed.SaveChangesAsync();

        var availability = CreateAvailability();

        (await availability.FindPackageAsync(false, "okko", "okko-95", 10m))!.PackageId.Should().Be("real-10l");
        (await availability.FindPackageAsync(true, "okko", "okko-95", 10m))!.PackageId.Should().Be("qa-mirror-10l");
    }

    /// <summary>
    /// Two volumes on one key cannot coexist any more, and the database is what says so.
    /// </summary>
    /// <remarks>
    /// This test used to seed two rows for the same (station, fuel, litres) in the same pool and assert
    /// that the resolver picked the newer one deterministically. That was guarding against a real defect
    /// - <c>BrandsController</c> added a nominal through a read-then-write with no lock, so two
    /// concurrent PUTs could both insert - and the ordering was the mitigation.
    /// <para>
    /// The split removed the possibility rather than the symptom. <c>UNIQUE (station_id, fuel_type_id,
    /// data_kind)</c> on fuel_offers times <c>UNIQUE (offer_id, liters)</c> on fuel_offer_volumes makes
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
    public async Task A_DuplicateVolumeOnOneKey_IsRefusedByTheDatabase()
    {
        using var seed = CreateContext();
        await TruncatePackagesAsync(seed);

        SeedPackage(seed, "first", isTestData: false, liters: 20m, price: 1780m, updatedMinutesAgo: 120);
        await seed.SaveChangesAsync();

        // Same offer (same station, fuel and pool) and the same nominal - the exact row the old
        // read-then-write race produced.
        SeedPackage(seed, "second", isTestData: false, liters: 20m, price: 1781m, updatedMinutesAgo: 1);

        var act = async () => await seed.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        // And with the duplicate refused, the key resolves to the one row that exists - no ordering
        // involved, because there is nothing to order.
        var availability = CreateAvailability();
        var chosen = await availability.FindPackageAsync(false, "okko", "okko-95", 20m);
        chosen!.PackageId.Should().Be("first");

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

        SeedPackage(seed, "qa-only", isTestData: true, liters: 5m, price: 100m, updatedMinutesAgo: 0);
        await seed.SaveChangesAsync();

        var availability = CreateAvailability();

        (await availability.FindPackageAsync(false, "okko", "okko-95", 5m)).Should().BeNull(
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

        SeedPackage(seed, "real-2l", isTestData: false, liters: 2m, price: 178.12m, updatedMinutesAgo: 5);
        SeedPackage(seed, "real-5l", isTestData: false, liters: 5m, price: 445.30m, updatedMinutesAgo: 5);
        // In the catalogue, but not in the cart - must not come back.
        SeedPackage(seed, "real-50l", isTestData: false, liters: 50m, price: 4453m, updatedMinutesAgo: 5);

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
            """TRUNCATE TABLE "fuel_offer_volumes", "fuel_offers", "fuel_vouchers", "orders", "order_line_items", "fulfillments", "outbox_events" RESTART IDENTITY CASCADE""");

    /// <summary>
    /// Seeds one sellable package as an offer plus a volume.
    /// </summary>
    /// <remarks>
    /// The offer is found by its key rather than created blindly, because two packages in one pool are
    /// two VOLUMES of one offer - that is the shape the uniqueness constraint demands, and seeding them
    /// as two offers would test a database state the application can never produce.
    /// </remarks>
    private static void SeedPackage(
        ApplicationDbContext seed,
        string id,
        bool isTestData,
        decimal liters,
        decimal price,
        int updatedMinutesAgo)
    {
        var pool = DataKinds.ForPool(isTestData);
        var offerId = OfferKeys.For("okko-95", pool);

        if (!seed.FuelOffers.Local.Any(o => o.Id == offerId) && !seed.FuelOffers.Any(o => o.Id == offerId))
        {
            seed.FuelOffers.Add(new FuelOffer
            {
                Id = offerId,
                StationId = "okko",
                FuelTypeId = "okko-95",
                DataKind = pool,
                // A flat cost and margin, independent of the volume's own price: what these tests
                // assert is which keys come back and what each volume costs, and deriving a margin from
                // the volume price made the QA fixture (1 UAH for 10 L) ask for a negative one, which the
                // database refuses - correctly, since a negative margin is a loss on every litre.
                SupplierPricePerLiter = 30m,
                MarginUahPerLiter = 20m,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-updatedMinutesAgo),
            });
        }

        seed.FuelOfferVolumes.Add(new FuelOfferVolume
        {
            Id = id,
            OfferId = offerId,
            Liters = liters,
            PriceOverrideUah = price,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-updatedMinutesAgo),
        });
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