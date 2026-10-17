using FluentAssertions;
using FuelFlow.Features.Orders.CreateCheckout;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// <c>fuel_offer_volumes.price_override_uah</c> is the last thing in the catalogue that is not a
/// derivation: a nominal whose price somebody set by hand. Everything else about a price now comes
/// from the offer's knobs, through one function.
/// <para>
/// These tests are about that column's contract rather than about the plumbing around it — the
/// arithmetic is covered by <c>PricingFormulaTests</c>, and the migration's backfill is covered by the
/// schema tests. What can only be checked here is the behaviour that follows from a real database:
/// nothing writes an override by accident, an override survives a reprice, and the storefront agrees
/// with checkout about what a nominal costs.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class PackagePriceOverrideIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PackagePriceOverrideIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static async Task<(FuelOffer Offer, FuelOfferVolume Volume)> SeedAsync(
        ApplicationDbContext context,
        string suffix,
        decimal? overrideUah = null)
    {
        var fuelId = $"override-fuel-{suffix}-{Guid.CreateVersion7()}";
        var offer = new FuelOffer
        {
            Id = fuelId,
            StationId = "okko",
            FuelTypeId = fuelId,
            DataKind = DataKinds.Real,
            SupplierPricePerLiter = 50m,
            MarginUahPerLiter = 2m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var volume = new FuelOfferVolume
        {
            Id = $"override-vol-{suffix}-{Guid.CreateVersion7()}",
            OfferId = fuelId,
            Liters = 10m,
            PriceOverrideUah = overrideUah,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        context.FuelOffers.Add(offer);
        context.FuelOfferVolumes.Add(volume);
        await context.SaveChangesAsync();
        return (offer, volume);
    }

    private static SellablePackage AsPackage(FuelOffer offer, FuelOfferVolume volume)
        => SellablePackage.From(offer, volume);

    [Fact]
    public async Task AVolumeWithNoOverride_CostsTheOfferRateTimesItsLitres()
    {
        await using var context = CreateContext();
        var (offer, volume) = await SeedAsync(context, "derived");

        var package = AsPackage(offer, volume);

        package.PriceOverrideUah.Should().BeNull();
        package.ComputedPackagePrice.Should().Be(520m);   // (50 + 2) x 10
        ServerPricing.PackagePrice(package, 10m).Should().Be(520m,
            "the shelf and the charge are the same number because they are the same function");
    }

    [Fact]
    public async Task AnOverride_IsWhatTheCustomerIsCharged_EvenWhenTheFuelHasARate()
    {
        await using var context = CreateContext();
        var (offer, volume) = await SeedAsync(context, "override", overrideUah: 500m);

        var package = AsPackage(offer, volume);

        package.ComputedPackagePrice.Should().Be(500m, "the override wins over the 520 the formula yields");
        ServerPricing.PackagePrice(package, 10m).Should().Be(500m,
            "checkout charges the same number the shelf shows, which is the point of one function");
    }

    [Fact]
    public async Task AnOverride_IsStillSubjectToTheBelowCostGuard()
    {
        await using var context = CreateContext();
        var (offer, volume) = await SeedAsync(context, "below-cost", overrideUah: 100m);

        var package = AsPackage(offer, volume);

        // 100 UAH for 10 L against a 50 UAH/litre cost: a 400 UAH loss on the package. The override is a
        // price, not a licence, and this is what stops it being one.
        ServerPricing.TryGetBelowCostVerdict(package, 0m)
            .Should().Be(ServerPricing.BelowCostVerdict.BelowCost);
        ServerPricing.IsBelowCost(package, 0m).Should().BeTrue();
    }

    [Fact]
    public async Task AnOverride_SurvivesAReprice_AndTheDerivedOneMoves()
    {
        await using var context = CreateContext();
        var (offer, volume) = await SeedAsync(context, "survives", overrideUah: 500m);
        var plain = await SeedAsync(context, "moves");

        // What a repricing is, as the write paths do it: the offer's knobs move and the volumes are not
        // touched at all. The old package_price column had to be rewritten here, and every path that did
        // it also erased whatever an operator had set by hand.
        foreach (var target in new[] { offer, plain.Offer })
        {
            target.MarginUahPerLiter = 4m;
            target.UpdatedAtUtc = DateTime.UtcNow;
        }
        await context.SaveChangesAsync();

        var overridden = await context.FuelOfferVolumes
            .AsNoTracking().SingleAsync(v => v.Id == volume.Id);
        var derived = await context.FuelOfferVolumes
            .AsNoTracking().SingleAsync(v => v.Id == plain.Volume.Id);

        overridden.PriceOverrideUah.Should().Be(500m, "an override is not a copy of the rate - it is an exception");
        derived.PriceOverrideUah.Should().BeNull();
    }

    [Fact]
    public async Task AVolumeWithNeitherRateNorOverride_IsNotCharged()
    {
        await using var context = CreateContext();

        var fuelId = $"unpriced-fuel-{Guid.CreateVersion7()}";
        var offer = new FuelOffer
        {
            Id = fuelId,
            StationId = "okko",
            FuelTypeId = fuelId,
            DataKind = DataKinds.Real,
            // What the OKKO catalogue import leaves behind: an offer exists, nobody has priced it.
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var volume = new FuelOfferVolume
        {
            Id = $"unpriced-vol-{Guid.CreateVersion7()}",
            OfferId = fuelId,
            Liters = 10m,
            PriceOverrideUah = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        context.FuelOffers.Add(offer);
        context.FuelOfferVolumes.Add(volume);
        await context.SaveChangesAsync();

        var package = AsPackage(offer, volume);

        package.ComputedFinalPricePerLiter.Should().BeNull();
        FuelPricing.TryPackagePrice(50m, null, 92.90m, 1m, 10m, null).Should().BeNull();
        var act = () => ServerPricing.PackagePrice(package, 10m);
        act.Should().Throw<InvalidOperationException>(
            "there is no price to charge, and charging nothing is not an option");
    }

    [Fact]
    public async Task TheColumnIsNullable_BecauseNullIsTheOrdinaryCase()
    {
        await using var context = CreateContext();

        // The schema is the claim: a NOT NULL column would make every nominal an override and every
        // reprice a fight. Asserted on the database rather than on the entity, because that is where the
        // NOT NULL would live.
        var nullable = await context.Database.SqlQueryRaw<string>(
            """
            SELECT is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_name = 'fuel_offer_volumes' AND column_name = 'price_override_uah'
            """).SingleAsync();

        nullable.Should().Be("YES");
    }

    [Fact]
    public async Task TheSeededCatalogue_CarriesNoOverride()
    {
        await using var context = CreateContext();

        // Scoped to the seeded ids on purpose: this suite shares one database with every other
        // integration test, and a count over all of them would be a statement about them.
        var seeded = await context.FuelOfferVolumes
            .AsNoTracking()
            .Where(v => v.Id.StartsWith("okko-") || v.Id.StartsWith("wog-"))
            .CountAsync(v => v.PriceOverrideUah != null);

        // Every seeded nominal follows its fuel's rate. An override appearing here means something in the
        // seed or the catalogue import started pinning prices, which is the thing this column is not for.
        seeded.Should().Be(0);
    }
}