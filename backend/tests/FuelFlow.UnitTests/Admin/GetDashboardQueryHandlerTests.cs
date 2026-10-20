using FluentAssertions;
using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Admin;

/// <summary>
/// The dashboard's revenue figure.
///
/// It used to load every fulfilled order and every one of its line items into the process to add
/// them up, which is a read that grows with every sale. It is now one SUM in the database, and
/// these pin the number it produces - the margin of the fuel, times the litres, times the
/// quantity, truncated to whole kopecks per line - and the two things that must not be counted: a
/// company handover (the platform never earned that margin) and a line whose size is not a real
/// sale size.
/// </summary>
public sealed class GetDashboardQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetDashboardQueryHandler _handler;

    public GetDashboardQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new GetDashboardQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task The_revenue_is_the_margin_of_the_fuel_times_the_litres()
    {
        var fuelTypeId = await SeedCatalogueAsync(marginPerLiter: 2.5m);

        await SeedOrderAsync(fuelTypeId, liters: 50, quantity: 2, status: OrderStatus.Fulfilled);

        var result = await _handler.HandleAsync(new GetDashboardQuery(), CancellationToken.None);

        // 2.5 UAH per litre x 50 litres x 2 = 250 UAH, which is 25000 kopecks - and the field
        // carries KOPIECKS (the name says Uah; the value has always been kopecks, and nothing
        // consumes the endpoint, so the unit is recorded here rather than changed).
        result.Orders.RevenueUah.Should().Be(250);
    }

    [Fact]
    public async Task A_company_handover_is_not_revenue()
    {
        var fuelTypeId = await SeedCatalogueAsync(marginPerLiter: 2.5m);

        await SeedOrderAsync(fuelTypeId, liters: 50, quantity: 2, status: OrderStatus.Fulfilled, kind: OrderKind.ReceivedFromCompany);

        var result = await _handler.HandleAsync(new GetDashboardQuery(), CancellationToken.None);

        result.Orders.RevenueUah.Should().Be(0);
        result.Orders.Total.Should().Be(0);
    }

    [Fact]
    public async Task A_line_whose_size_is_not_a_real_sale_size_has_no_margin()
    {
        var fuelTypeId = await SeedCatalogueAsync(marginPerLiter: 2.5m);

        // The size has no row in the real pool, so the margin lookup cannot answer for it.
        await SeedOrderAsync(fuelTypeId, liters: 77, quantity: 1, status: OrderStatus.Fulfilled);

        var result = await _handler.HandleAsync(new GetDashboardQuery(), CancellationToken.None);

        result.Orders.RevenueUah.Should().Be(0);
    }

    [Fact]
    public async Task An_unfulfilled_order_is_not_revenue()
    {
        var fuelTypeId = await SeedCatalogueAsync(marginPerLiter: 2.5m);

        await SeedOrderAsync(fuelTypeId, liters: 50, quantity: 2, status: OrderStatus.PendingFulfillment);

        var result = await _handler.HandleAsync(new GetDashboardQuery(), CancellationToken.None);

        result.Orders.RevenueUah.Should().Be(0);
    }

    private async Task<Guid> SeedCatalogueAsync(decimal marginPerLiter)
    {
        var brandId = Guid.NewGuid();
        var fuelTypeId = Guid.NewGuid();

        _context.Brands.Add(new Brand
        {
            Id = brandId,
            Slug = "okko",
            Name = "OKKO",
            LogoText = "OK",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _context.FuelTypes.Add(new FuelTypeEntity
        {
            Id = fuelTypeId,
            Name = "Diesel",
            BrandId = brandId,
            BasePrice = 50m,
            MarginUahPerLiter = marginPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        _context.FuelNominals.Add(new FuelNominal
        {
            FuelTypeId = fuelTypeId,
            DataKind = DataKinds.Real,
            Liters = 50,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
        return fuelTypeId;
    }

    private async Task SeedOrderAsync(
        Guid fuelTypeId,
        decimal liters,
        int quantity,
        OrderStatus status,
        OrderKind kind = OrderKind.Purchase)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Price = 100m,
            Status = status,
            Kind = kind,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Brand = "okko",
            FuelTypeId = fuelTypeId,
            Liters = liters,
            Quantity = quantity,
            UnitPrice = 50m,
            LineTotal = 100m
        });

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
    }
}
