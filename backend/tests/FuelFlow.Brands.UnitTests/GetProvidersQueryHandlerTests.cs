using FuelFlow.Brands.UnitTests;
using FluentAssertions;
using FuelFlow.Features.Brands.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.Providers.UnitTests;

public sealed class GetBrandsQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetBrandsQueryHandler _handler;

    /// <summary>A brand row needs an id AND its network code; the fixture states both.</summary>
    private static readonly Guid TestOkko = new("0f0f0f0f-0000-4000-8000-00000000000f");
    private static readonly Guid TestKlo = new("0f0f0f0f-0000-4000-8000-0000000000f1");

    public GetBrandsQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();

        _handler = new GetBrandsQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Handle_AFuelWithNoSizes_StillShowsTheColumnPrice()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        // Exactly what the catalogue import leaves behind: a fuel with the published pump price, no
        // cost, and - because nobody has set a size yet - no sizes either.
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("test-fuel-imported"),
            Name = "А-95",
            BrandId = TestOkko,
            PumpPricePerLiter = 92.90m,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetBrandsQuery());

        var fuel = providers.Single(p => p.Id == TestOkko).Fuels.Single(f => f.Id == ToUuid("test-fuel-imported").ToString());
        fuel.PumpPricePerLiter.Should().Be(92.90m, "the card reads the fuel's own columns, not its sizes");
        fuel.FinalPricePerLiter.Should().Be(92.90m, "the column price stands in until there is a cost");
        fuel.SupplierPricePerLiter.Should().BeNull(
            "there is no cost yet, and the card says so - NOT zero, because a zero here is written "
            + "straight back to the fuel by the save that posts this card, and a fuel with a cost of "
            + "zero is priced at its margin and has no below-cost guard");
        fuel.PackageLiters.Should().BeEmpty("a fuel with no sizes has no sizes to sell");
    }

    [Fact]
    public async Task Handle_ReturnsFuelsWithPricingAndDistinctNominals()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = ToUuid("test-fuel-a"), Name = "A-95", BrandId = TestOkko, BasePrice = 5100, DiscountPrice = 5100, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = ToUuid("test-fuel-b"), Name = "ДП", BrandId = TestOkko, BasePrice = 5200, DiscountPrice = 5200, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.SeedPackage("test-fuel-a-10", TestOkko, "test-fuel-a", 10m, 510m,
            supplierPricePerLiter: 49m, marginUahPerLiter: 2m, finalPricePerLiter: 51m);
        _context.SeedPackage("test-fuel-a-20", TestOkko, "test-fuel-a", 20m, 1020m,
            supplierPricePerLiter: 49m, marginUahPerLiter: 2m, finalPricePerLiter: 51m);
        _context.SeedPackage("test-fuel-b-20", TestOkko, "test-fuel-b", 20m, 1040m,
            supplierPricePerLiter: 50m, marginUahPerLiter: 2m, finalPricePerLiter: 52m);
        _context.SeedPackage("test-fuel-b-50", TestOkko, "test-fuel-b", 50m, 2600m,
            supplierPricePerLiter: 50m, marginUahPerLiter: 2m, finalPricePerLiter: 52m);
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetBrandsQuery());

        var brand = providers.Single(p => p.Id == TestOkko);
        brand.Nominals.Should().Equal(10, 20, 50);

        brand.Fuels.Should().HaveCount(2);

        var fuelA = brand.Fuels.Single(f => f.Id == ToUuid("test-fuel-a").ToString());
        fuelA.SupplierPricePerLiter.Should().Be(49m);
        fuelA.MarginUahPerLiter.Should().Be(2m);
        fuelA.FinalPricePerLiter.Should().Be(51m);
        fuelA.PackageLiters.Should().Equal(10, 20);
        // Every package agrees, so there is nothing to warn about.
        fuelA.PriceSpread.Should().BeNull();
    }

    // The card never carried the code, so every fuel serialised as null and the admin screen had
    // nothing to show. That matters beyond cosmetics: the code is what the catalogue import matches
    // on, so a fuel whose code the operator cannot see is a fuel they cannot tell apart from a
    // near-duplicate of it - which is how the same grade gets typed in twice.
    [Fact]
    public async Task Handle_CarriesTheBrandFuelCode_AndNullWhenThereIsNone()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = ToUuid("test-coded"), Name = "А-95", BrandId = TestOkko, BrandFuelCode = "A-95", BasePrice = 92.9m, DiscountPrice = 89.06m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = ToUuid("test-uncoded"), Name = "А 92", BrandId = TestOkko, BrandFuelCode = null, BasePrice = 85m, DiscountPrice = 84.5m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetBrandsQuery());

        var brand = providers.Single(p => p.Id == TestOkko);
        brand.Fuels.Single(f => f.Id == ToUuid("test-coded").ToString()).BrandFuelCode.Should().Be("A-95");
        // Null must stay null rather than becoming "" - the screen tells the two apart, and the API
        // reads an empty string on a write as "clear this".
        brand.Fuels.Single(f => f.Id == ToUuid("test-uncoded").ToString()).BrandFuelCode.Should().BeNull();
    }

    // The production case from #202: four packages for one fuel, one of them a 0.01 L leftover with a
    // cost nothing like the others. The card used to report whichever row the database returned first,
    // so it showed 5.00 for a fuel whose real packages all cost 100.00 - and which the below-cost
    // guard then refused to sell. The card has to describe the fuel, not one nominal of it.
    [Fact]
    public async Task Handle_CostIsLitersWeightedAcrossPackages_NotOneArbitraryRow()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("test-dp"),
            Name = "ДП ЄВРО",
            BrandId = TestOkko,
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelNominals.AddRange(
            // The QA leftover, inserted first so an unordered read would pick it.
            Package("test-dp-001", 0.01m, cost: 5.00m, final: 10.00m),
            Package("test-dp-2", 2m, cost: 100.00m, final: 99.90m),
            Package("test-dp-3", 3m, cost: 100.00m, final: 99.90m),
            Package("test-dp-10", 10m, cost: 100.00m, final: 99.90m));
        await _context.SaveChangesAsync();
        var providers = await _handler.HandleAsync(new GetBrandsQuery());
        var fuel = providers.Single(p => p.Id == TestOkko).Fuels.Single();

        // The card reads ONE price per fuel, from the offer. The 0.01 L leftover is now a volume of the
        // same offer as every other nominal rather than a row carrying a competing cost, so it can
        // neither drag the fuel's cost to 5.00 nor register as an outlier at all. The two behaviours
        // this test used to assert - a litres-weighted average across disagreeing rows, and a spread
        // that made the outlier visible - are the two the split removed on purpose: disagreement
        // between the packages of one fuel is no longer representable in the schema.
        fuel.SupplierPricePerLiter.Should().Be(100m);
        fuel.FinalPricePerLiter.Should().Be(99.90m);

        // Nothing to spread: one offer, one price. A spread here would mean two prices for one fuel
        // had crept back in, which is exactly what this is guarding against.
        fuel.PriceSpread.Should().BeNull();
        fuel.PackageLiters.Should().BeEquivalentTo(new[] { 0, 2, 3, 10 });
    }

    /// <summary>
    /// A fuel cannot disagree with itself any more.
    /// </summary>
    /// <remarks>
    /// This test existed to catch two packages of one fuel carrying different per-litre prices, and to
    /// require the card to surface the disagreement via <c>PriceSpread</c> so an operator could see it
    /// before the write path flattened it. The split makes that state unrepresentable: one fuel at one
    /// Brand owns one offer, and every volume of that offer reads the same price. The second package
    /// seeded here therefore lands on the SAME offer, and the last write of cost wins - which is the
    /// behaviour worth pinning, because it is the opposite of the old weighted average and would be a
    /// silent regression if someone reintroduced per-package costs.
    /// </remarks>
    [Fact]
    public async Task Handle_AFuelHasOnePrice_BecauseItHasOneOffer()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("test-dp"),
            Name = "ДП ЄВРО",
            BrandId = TestOkko,
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        // Two sizes of ONE offer. The offer carries the price; the sizes are the fuel's, so the card
        // reports one price per fuel and lists both sizes.
        _context.FuelNominals.AddRange(
            Package("test-dp-2", 2m, cost: 100.00m, final: 99.90m),
            Package("test-dp-10", 10m, cost: 110.00m, final: 105.00m));
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetBrandsQuery());
        var fuel = providers.Single(p => p.Id == TestOkko).Fuels.Single();

        // The spread is null because there is nothing to spread. If this ever becomes non-null, the
        // data is genuinely disagreeing again and the operator is meant to be told.
        fuel.PriceSpread.Should().BeNull();

        // Both volumes are still sellable, and both are priced off the same offer.
        fuel.PackageLiters.Should().BeEquivalentTo(new[] { 2, 10 });
        fuel.SupplierPricePerLiter.Should().Be(110.00m);   // last write on the offer wins
        fuel.FinalPricePerLiter.Should().Be(105.00m);
    }

    // Two page loads must not disagree with each other. `FirstOrDefault()` over an unordered result
    // could return a different row each time, which is the specific defect #202 reported.
    [Fact]
    public async Task Handle_RepeatedReads_AreIdentical()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestOkko,
            Slug = "test-okko",
            Name = "okko",
            LogoText = "okko",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = ToUuid("test-dp"),
            Name = "ДП ЄВРО",
            BrandId = TestOkko,
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        // cost 100 + margin 1 = 101, and the ceiling (pump 105 - 0.5) does not bind, so the customer price
        // is 101. This fixture used to state 99.90, which its own cost and margin did not produce - a
        // discrepancy the seeder now refuses, and the reason it does: a fixture that disagrees with the
        // formula proves nothing about the rows the application would really write.
        _context.FuelNominals.AddRange(
            Package("test-dp-a", 2m, cost: 100.00m, final: 101m, margin: 1.00m, pump: 105m),
            Package("test-dp-b", 5m, cost: 100.00m, final: 101m, margin: 1.00m, pump: 105m),
            Package("test-dp-c", 9m, cost: 100.00m, final: 101m, margin: 1.00m, pump: 105m));
        await _context.SaveChangesAsync();

        var first = await _handler.HandleAsync(new GetBrandsQuery());
        var second = await _handler.HandleAsync(new GetBrandsQuery());

        second.SelectMany(p => p.Fuels).Should().BeEquivalentTo(first.SelectMany(p => p.Fuels));
    }

    /// <summary>
    /// Seeds the offer behind the fixture and returns its volume.
    /// </summary>
    /// <remarks>
    /// Returns the volume rather than adding it, so every <c>AddRange(Package(...))</c> below keeps
    /// working with only the DbSet renamed. The per-litre figures move to the offer because that is
    /// where the brand card now reads them: one price per litre per fuel, not one copy per package.
    /// </remarks>
private FuelNominal Package(
        string id, decimal liters, decimal cost, decimal final,
        decimal? margin = null, decimal? pump = null)
    {
    _context.SeedOffer(
        TestOkko, "test-dp",
        supplierPricePerLiter: cost,
        marginUahPerLiter: margin ?? (final - cost),
        finalPricePerLiter: final,
        pumpPricePerLiter: pump,
        minDiscountPerLiter: 0.5m);

    // The size, keyed by fuel and litres. The offer supplies the price; the nominal says the size
    // is sold at all, and it is the same row whichever pool reads it.
    return new FuelNominal
    {
        FuelTypeId = ToUuid("test-dp"),
        Liters = liters,
        CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    [Fact]
    public async Task Handle_ProviderWithoutFuels_HasEmptyFuelsAndNominals()
    {
        _context.Brands.Add(new Brand
        {
            Id = TestKlo,
            Slug = "test-klo",
            Name = "klo",
            LogoText = "klo",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetBrandsQuery());

        var brand = providers.Single(p => p.Id == TestKlo);
        brand.Fuels.Should().BeEmpty();
        brand.Nominals.Should().BeEmpty();
    }
}
