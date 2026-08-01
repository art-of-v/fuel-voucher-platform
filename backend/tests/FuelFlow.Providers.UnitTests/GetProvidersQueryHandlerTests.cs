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
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
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
    }

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
