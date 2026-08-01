using FluentAssertions;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Providers.GetProviderById;
using FuelFlow.Features.Providers.GetProviderHistory;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Providers.UnitTests;

public sealed class GetProviderByIdAndHistoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public GetProviderByIdAndHistoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // --- GetProviderByIdQueryHandler ---------------------------------------

    [Fact]
    public async Task GetProviderById_ShouldReturnProvider()
    {
        var now = DateTime.UtcNow;
        _context.Stations.Add(new Station
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#22c55e",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "test-fuel-a", Name = "A-95", StationId = "test-okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelTypeEntity { Id = "test-fuel-b", Name = "DP", StationId = "test-okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now });
        _context.FuelPackages.AddRange(
            new FuelPackage { Id = "test-fuel-a-10", StationId = "test-okko", FuelTypeId = "test-fuel-a", FuelName = "A-95", Liters = 10m, Price = 510, OriginalPrice = 490, SupplierPricePerLiter = 49m, MarginUahPerLiter = 2m, FinalPricePerLiter = 51m, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelPackage { Id = "test-fuel-a-20", StationId = "test-okko", FuelTypeId = "test-fuel-a", FuelName = "A-95", Liters = 20m, Price = 1020, OriginalPrice = 980, SupplierPricePerLiter = 49m, MarginUahPerLiter = 2m, FinalPricePerLiter = 51m, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelPackage { Id = "test-fuel-b-50", StationId = "test-okko", FuelTypeId = "test-fuel-b", FuelName = "DP", Liters = 50m, Price = 2600, OriginalPrice = 2500, SupplierPricePerLiter = 50m, MarginUahPerLiter = 2m, FinalPricePerLiter = 52m, CreatedAtUtc = now, UpdatedAtUtc = now });
        await _context.SaveChangesAsync();

        var handler = new GetProviderByIdQueryHandler(_context);
        var provider = await handler.HandleAsync(new GetProviderByIdQuery("test-okko"), CancellationToken.None);

        provider.Should().NotBeNull();
        provider!.Id.Should().Be("test-okko");
        provider.Name.Should().Be("OKKO");
        provider.LogoText.Should().Be("OKKO");
        provider.Color.Should().Be("#22c55e");
        provider.Nominals.Should().Equal(10, 20, 50);
        provider.Fuels.Should().HaveCount(2);

        var fuelA = provider.Fuels.Single(f => f.Id == "test-fuel-a");
        fuelA.Name.Should().Be("A-95");
        fuelA.SupplierPricePerLiter.Should().Be(49m);
        fuelA.MarginUahPerLiter.Should().Be(2m);
        fuelA.FinalPricePerLiter.Should().Be(51m);
        fuelA.PackageLiters.Should().Equal(10, 20);
    }

    [Fact]
    public async Task GetProviderById_ShouldReturnNull_WhenStationMissing()
    {
        var handler = new GetProviderByIdQueryHandler(_context);
        var result = await handler.HandleAsync(new GetProviderByIdQuery("missing"), CancellationToken.None);

        result.Should().BeNull();
    }

    // --- GetProviderHistoryQueryHandler ------------------------------------

    private ProviderEventOutbox CreateEvent(string providerId, DateTime changedAt, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            AggregateType = "Fuel",
            AggregateId = "fuel-a",
            ProviderId = providerId,
            EventType = "PriceChanged",
            OldValue = null,
            NewValue = value,
            ChangedByUserId = Guid.NewGuid(),
            ChangedByUserName = "Admin",
            Summary = "summary",
            ChangedAtUtc = changedAt
        };

    [Fact]
    public async Task GetProviderHistory_ShouldReturnEvents_ForProvider()
    {
        var now = DateTime.UtcNow;
        var e1 = CreateEvent("test-okko", now.AddMinutes(-3), "v1");
        var e2 = CreateEvent("test-okko", now.AddMinutes(-1), "v2");
        var e3 = CreateEvent("test-okko", now.AddMinutes(-2), "v3");
        var other = CreateEvent("test-wog", now, "v4");
        _context.ProviderEventOutbox.AddRange(e1, e2, e3, other);
        await _context.SaveChangesAsync();

        var handler = new GetProviderHistoryQueryHandler(_context);
        var result = await handler.HandleAsync(new GetProviderHistoryQuery("test-okko"), CancellationToken.None);

        result.Should().HaveCount(3);
        result.Select(e => e.Id).Should().Equal(e2.Id, e3.Id, e1.Id);

        var first = result[0];
        first.AggregateType.Should().Be("Fuel");
        first.AggregateId.Should().Be("fuel-a");
        first.EventType.Should().Be("PriceChanged");
        first.ChangedByUserName.Should().Be("Admin");
        first.Summary.Should().Be("summary");
        first.ChangedAtUtc.Should().Be(e2.ChangedAtUtc);
    }

    [Fact]
    public async Task GetProviderHistory_ShouldRespectLimit()
    {
        var now = DateTime.UtcNow;
        var events = new[]
        {
            CreateEvent("test-okko", now.AddMinutes(-5), "v1"),
            CreateEvent("test-okko", now.AddMinutes(-4), "v2"),
            CreateEvent("test-okko", now.AddMinutes(-3), "v3"),
            CreateEvent("test-okko", now.AddMinutes(-2), "v4"),
            CreateEvent("test-okko", now.AddMinutes(-1), "v5")
        };
        _context.ProviderEventOutbox.AddRange(events);
        await _context.SaveChangesAsync();

        var handler = new GetProviderHistoryQueryHandler(_context);
        var result = await handler.HandleAsync(new GetProviderHistoryQuery("test-okko", 2), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(e => e.Id).Should().Equal(events[4].Id, events[3].Id);
    }

    // --- ProviderEventService ----------------------------------------------

    [Fact]
    public async Task RecordEventAsync_ShouldPersistEvent()
    {
        var changedByUserId = Guid.NewGuid();
        var service = new ProviderEventService(_context);

        await service.RecordEventAsync(
            "Fuel",
            "fuel-a",
            "PriceChanged",
            "49.00",
            "51.00",
            changedByUserId,
            "Admin User",
            "Price updated",
            "test-okko",
            CancellationToken.None);

        var evt = await _context.ProviderEventOutbox.SingleAsync();
        evt.AggregateType.Should().Be("Fuel");
        evt.AggregateId.Should().Be("fuel-a");
        evt.ProviderId.Should().Be("test-okko");
        evt.EventType.Should().Be("PriceChanged");
        evt.OldValue.Should().Be("49.00");
        evt.NewValue.Should().Be("51.00");
        evt.ChangedByUserId.Should().Be(changedByUserId);
        evt.ChangedByUserName.Should().Be("Admin User");
        evt.Summary.Should().Be("Price updated");
        evt.ChangedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
