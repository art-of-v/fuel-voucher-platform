using FluentAssertions;
using FuelFlow.Features.Providers.GetProviders;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Providers.UnitTests;

public sealed class GetProvidersQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetProvidersQueryHandler _handler;

    public GetProvidersQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _handler = new GetProvidersQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task Handle_ReturnsFuelsWithPricingAndDistinctNominals()
    {
        _context.Stations.Add(new Station
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "test-fuel-a", Name = "A-95", StationId = "test-okko", BasePrice = 5100, DiscountPrice = 5100, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelTypeEntity { Id = "test-fuel-b", Name = "ДП", StationId = "test-okko", BasePrice = 5200, DiscountPrice = 5200, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.FuelPackages.AddRange(
            new FuelPackage { Id = "test-fuel-a-10", StationId = "test-okko", FuelTypeId = "test-fuel-a", FuelName = "A-95", Liters = 10m, Price = 510, OriginalPrice = 490, SupplierPricePerLiter = 49m, MarginUahPerLiter = 2m, FinalPricePerLiter = 51m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelPackage { Id = "test-fuel-a-20", StationId = "test-okko", FuelTypeId = "test-fuel-a", FuelName = "A-95", Liters = 20m, Price = 1020, OriginalPrice = 980, SupplierPricePerLiter = 49m, MarginUahPerLiter = 2m, FinalPricePerLiter = 51m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelPackage { Id = "test-fuel-b-20", StationId = "test-okko", FuelTypeId = "test-fuel-b", FuelName = "ДП", Liters = 20m, Price = 1040, OriginalPrice = 1000, SupplierPricePerLiter = 50m, MarginUahPerLiter = 2m, FinalPricePerLiter = 52m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new FuelPackage { Id = "test-fuel-b-50", StationId = "test-okko", FuelTypeId = "test-fuel-b", FuelName = "ДП", Liters = 50m, Price = 2600, OriginalPrice = 2500, SupplierPricePerLiter = 50m, MarginUahPerLiter = 2m, FinalPricePerLiter = 52m, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetProvidersQuery());

        var provider = providers.Single(p => p.Id == "test-okko");
        provider.Nominals.Should().Equal(10, 20, 50);

        provider.Fuels.Should().HaveCount(2);

        var fuelA = provider.Fuels.Single(f => f.Id == "test-fuel-a");
        fuelA.SupplierPricePerLiter.Should().Be(49m);
        fuelA.MarginUahPerLiter.Should().Be(2m);
        fuelA.FinalPricePerLiter.Should().Be(51m);
        fuelA.PackageLiters.Should().Equal(10, 20);
        // Every package agrees, so there is nothing to warn about.
        fuelA.PriceSpread.Should().BeNull();
    }

    // The production case from #202: four packages for one fuel, one of them a 0.01 L leftover with a
    // cost nothing like the others. The card used to report whichever row the database returned first,
    // so it showed 5.00 for a fuel whose real packages all cost 100.00 - and which the below-cost
    // guard then refused to sell. The card has to describe the fuel, not one nominal of it.
    [Fact]
    public async Task Handle_CostIsLitersWeightedAcrossPackages_NotOneArbitraryRow()
    {
        _context.Stations.Add(new Station
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "test-dp",
            Name = "ДП ЄВРО",
            StationId = "test-okko",
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.AddRange(
            // The QA leftover, inserted first so an unordered read would pick it.
            Package("test-dp-001", 0.01m, cost: 5.00m, final: 10.00m),
            Package("test-dp-2", 2m, cost: 100.00m, final: 99.90m),
            Package("test-dp-3", 3m, cost: 100.00m, final: 99.90m),
            Package("test-dp-10", 10m, cost: 100.00m, final: 99.90m));
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetProvidersQuery());
        var fuel = providers.Single(p => p.Id == "test-okko").Fuels.Single();

        // (5.00×0.01 + 100×2 + 100×3 + 100×10) / 15.01 L = 99.9367. The QA leftover still moves the
        // average a little - weighting by litres is honest, pretending it does not exist is not -
        // but it can no longer drag the fuel's cost to 5.00 and make it look saleable when every
        // package a customer can actually buy costs 100.00.
        fuel.SupplierPricePerLiter.Should().Be(99.9367m);
        fuel.SupplierPricePerLiter.Should().BeGreaterThan(99m);
        fuel.SupplierPricePerLiter.Should().NotBe(5m);
        // The spread is what tells the operator the outlier is there at all.
        fuel.PriceSpread!.MinSupplierPricePerLiter.Should().Be(5m);
        fuel.PriceSpread.MaxSupplierPricePerLiter.Should().Be(100m);
    }

    // The write path rewrites every package of a fuel to whatever this card shows, so a silent
    // disagreement here is data the operator is about to flatten without being told.
    [Fact]
    public async Task Handle_ReportsTheSpread_WhenPackagesDisagree()
    {
        _context.Stations.Add(new Station
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "test-dp",
            Name = "ДП ЄВРО",
            StationId = "test-okko",
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.AddRange(
            Package("test-dp-2", 2m, cost: 100.00m, final: 99.90m),
            Package("test-dp-10", 10m, cost: 110.00m, final: 105.00m));
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetProvidersQuery());
        var fuel = providers.Single(p => p.Id == "test-okko").Fuels.Single();

        fuel.PriceSpread.Should().NotBeNull();
        fuel.PriceSpread!.MinSupplierPricePerLiter.Should().Be(100m);
        fuel.PriceSpread.MaxSupplierPricePerLiter.Should().Be(110m);
        fuel.PriceSpread.MinFinalPricePerLiter.Should().Be(99.90m);
        fuel.PriceSpread.MaxFinalPricePerLiter.Should().Be(105m);
    }

    // Two page loads must not disagree with each other. `FirstOrDefault()` over an unordered result
    // could return a different row each time, which is the specific defect #202 reported.
    [Fact]
    public async Task Handle_RepeatedReads_AreIdentical()
    {
        _context.Stations.Add(new Station
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = "test-dp",
            Name = "ДП ЄВРО",
            StationId = "test-okko",
            BasePrice = 110,
            DiscountPrice = 100,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.FuelPackages.AddRange(
            Package("test-dp-a", 2m, cost: 100.00m, final: 99.90m, margin: 1.00m, pump: 105m),
            Package("test-dp-b", 5m, cost: 100.00m, final: 99.90m, margin: 1.00m, pump: 105m),
            Package("test-dp-c", 9m, cost: 100.00m, final: 99.90m, margin: 1.00m, pump: 105m));
        await _context.SaveChangesAsync();

        var first = await _handler.HandleAsync(new GetProvidersQuery());
        var second = await _handler.HandleAsync(new GetProvidersQuery());

        second.SelectMany(p => p.Fuels).Should().BeEquivalentTo(first.SelectMany(p => p.Fuels));
    }

    private static FuelPackage Package(
        string id, decimal liters, decimal cost, decimal final,
        decimal? margin = null, decimal? pump = null) => new()
    {
        Id = id,
        StationId = "test-okko",
        FuelTypeId = "test-dp",
        FuelName = "ДП ЄВРО",
        Liters = liters,
        Price = final * liters,
        OriginalPrice = (pump ?? final) * liters,
        SupplierPricePerLiter = cost,
        MarginUahPerLiter = margin ?? (final - cost),
        FinalPricePerLiter = final,
        PumpPricePerLiter = pump,
        MinDiscountPerLiter = 0.5m,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_ProviderWithoutFuels_HasEmptyFuelsAndNominals()
    {
        _context.Stations.Add(new Station
        {
            Id = "test-klo",
            Name = "KLO",
            LogoText = "KLO",
            Color = "#fff",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var providers = await _handler.HandleAsync(new GetProvidersQuery());

        var provider = providers.Single(p => p.Id == "test-klo");
        provider.Fuels.Should().BeEmpty();
        provider.Nominals.Should().BeEmpty();
    }
}
