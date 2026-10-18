using FuelFlow.Brands.UnitTests;
using FluentAssertions;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Brands.GetProviderById;
using FuelFlow.Features.Brands.GetProviderHistory;
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
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    // --- GetBrandByIdQueryHandler ---------------------------------------

    [Fact]
    public async Task GetProviderById_ShouldReturnProvider()
    {
        var now = DateTime.UtcNow;
        _context.Brands.Add(new Brand
        {
            Id = "test-okko",
            Name = "OKKO",
            LogoText = "OKKO",
            Color = "#22c55e",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        _context.FuelTypes.AddRange(
            new FuelTypeEntity { Id = "test-fuel-a", Name = "A-95", BrandId = "test-okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now },
            new FuelTypeEntity { Id = "test-fuel-b", Name = "DP", BrandId = "test-okko", BasePrice = 100, DiscountPrice = 100, CreatedAtUtc = now, UpdatedAtUtc = now });
        _context.SeedPackage("test-fuel-a-10", "test-okko", "test-fuel-a", 10m, 510m,
            supplierPricePerLiter: 49m, marginUahPerLiter: 2m, finalPricePerLiter: 51m,
            createdAtUtc: now, updatedAtUtc: now);
        _context.SeedPackage("test-fuel-a-20", "test-okko", "test-fuel-a", 20m, 1020m,
            supplierPricePerLiter: 49m, marginUahPerLiter: 2m, finalPricePerLiter: 51m,
            createdAtUtc: now, updatedAtUtc: now);
        _context.SeedPackage("test-fuel-b-50", "test-okko", "test-fuel-b", 50m, 2600m,
            supplierPricePerLiter: 50m, marginUahPerLiter: 2m, finalPricePerLiter: 52m,
            createdAtUtc: now, updatedAtUtc: now);
        await _context.SaveChangesAsync();

        var handler = new GetBrandByIdQueryHandler(_context);
        var brand = await handler.HandleAsync(new GetBrandByIdQuery("test-okko"), CancellationToken.None);

        brand.Should().NotBeNull();
        brand!.Id.Should().Be("test-okko");
        brand.Name.Should().Be("OKKO");
        brand.LogoText.Should().Be("OKKO");
        brand.Color.Should().Be("#22c55e");
        brand.Nominals.Should().Equal(10, 20, 50);
        brand.Fuels.Should().HaveCount(2);

        var fuelA = brand.Fuels.Single(f => f.Id == "test-fuel-a");
        fuelA.Name.Should().Be("A-95");
        fuelA.SupplierPricePerLiter.Should().Be(49m);
        fuelA.MarginUahPerLiter.Should().Be(2m);
        fuelA.FinalPricePerLiter.Should().Be(51m);
        fuelA.PackageLiters.Should().Equal(10, 20);
    }

    [Fact]
    public async Task GetProviderById_ShouldReturnNull_WhenBrandMissing()
    {
        var handler = new GetBrandByIdQueryHandler(_context);
        var result = await handler.HandleAsync(new GetBrandByIdQuery("missing"), CancellationToken.None);

        result.Should().BeNull();
    }

    // --- GetBrandHistoryQueryHandler ------------------------------------

    private AuditEventOutbox CreateEvent(string providerId, DateTime changedAt, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            AggregateType = "Fuel",
            AggregateId = "fuel-a",
            ScopeId = providerId,
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
        _context.AuditEventOutbox.AddRange(e1, e2, e3, other);
        await _context.SaveChangesAsync();

        var handler = new GetBrandHistoryQueryHandler(_context);
        var result = await handler.HandleAsync(new GetBrandHistoryQuery("test-okko"), CancellationToken.None);

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
        _context.AuditEventOutbox.AddRange(events);
        await _context.SaveChangesAsync();

        var handler = new GetBrandHistoryQueryHandler(_context);
        var result = await handler.HandleAsync(new GetBrandHistoryQuery("test-okko", 2), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(e => e.Id).Should().Equal(events[4].Id, events[3].Id);
    }

    // --- AuditEventService ----------------------------------------------

    [Fact]
    public async Task RecordEventAsync_ShouldPersistEvent()
    {
        var changedByUserId = Guid.NewGuid();
        var service = new AuditEventService(_context);

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

        var evt = await _context.AuditEventOutbox.SingleAsync();
        evt.AggregateType.Should().Be("Fuel");
        evt.AggregateId.Should().Be("fuel-a");
        evt.ScopeId.Should().Be("test-okko");
        evt.EventType.Should().Be("PriceChanged");
        evt.OldValue.Should().Be("49.00");
        evt.NewValue.Should().Be("51.00");
        evt.ChangedByUserId.Should().Be(changedByUserId);
        evt.ChangedByUserName.Should().Be("Admin User");
        evt.Summary.Should().Be("Price updated");
        evt.ChangedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
