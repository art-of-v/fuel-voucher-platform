using FluentAssertions;
using FuelFlow.Features.Vouchers.Availability;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
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
            .Options);
}