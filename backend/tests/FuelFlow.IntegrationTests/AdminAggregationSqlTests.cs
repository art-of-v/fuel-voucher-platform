using FluentAssertions;
using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.GetInventory;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Two screens that used to read a whole table into the process and work it out there: the
/// inventory (every voucher, grouped in memory) and the dashboard (every fulfilled order and its
/// line items, added up in memory).
///
/// <b>Why these run against a real Postgres.</b> The unit tests run on the in-memory provider,
/// which evaluates a LINQ expression in the client and therefore proves nothing about the SQL the
/// provider would send. The GROUP BY with conditional counts, the four-table join, the correlated
/// subquery and <c>trunc()</c> are all things only the database can answer - and the failure mode
/// of getting them wrong is a screen that reports a wrong number, not an exception.
/// </summary>
[Collection("Integration Tests")]
public sealed class AdminAggregationSqlTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AdminAggregationSqlTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_dashboard_revenue_is_summed_by_the_database()
    {
        await using var context = CreateContext();

        var fuel = await context.FuelNominals.AsNoTracking()
            .Where(n => n.DataKind == DataKinds.Real)
            .Join(context.FuelTypes.AsNoTracking(), n => n.FuelTypeId, f => f.Id, (n, f) => new { n, f })
            .Join(context.Brands.AsNoTracking(), x => x.f.BrandId, b => b.Id, (x, b) => new { x.n, x.f, b })
            .FirstAsync();

        const decimal marginPerLiter = 2.5m;
        var fuelType = await context.FuelTypes.FirstAsync(f => f.Id == fuel.f.Id);
        fuelType.MarginUahPerLiter = marginPerLiter;

        var userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var orderId = Guid.NewGuid();
        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.Fulfilled,
            FulfilledAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            LineItems =
            [
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    BrandId = BrandIds.ForSlug(fuel.b.Slug),
                    Brand = fuel.b.Slug,
                    FuelTypeId = fuel.f.Id,
                    Liters = fuel.n.Liters,
                    Quantity = 2,
                    UnitPrice = 50m,
                    LineTotal = 100m
                }
            ]
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var result = await new GetDashboardQueryHandler(context)
            .HandleAsync(new GetDashboardQuery(), CancellationToken.None);

        // The margin per litre, times the litres, times the quantity, truncated to whole kopecks
        // per line - the same arithmetic the in-process version did.
        var expected = (long)Math.Truncate(marginPerLiter * fuel.n.Liters * 2);
        result.Orders.RevenueUah.Should().Be(expected);
    }

    [Fact]
    public async Task The_inventory_is_grouped_by_the_database()
    {
        await using var context = CreateContext();

        var fuel = await context.FuelNominals.AsNoTracking()
            .Where(n => n.DataKind == DataKinds.Real)
            .Join(context.FuelTypes.AsNoTracking(), n => n.FuelTypeId, f => f.Id, (n, f) => new { n, f })
            .Join(context.Brands.AsNoTracking(), x => x.f.BrandId, b => b.Id, (x, b) => new { x.n, x.f, b })
            .FirstAsync();

        var userId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = userId,
            PhoneNumber = $"+38{userId:N}"[..20],
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        var orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 100m,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        context.FuelVouchers.AddRange(
            Voucher(fuel.b.Slug, fuel.f.Id, fuel.n.Liters, VoucherStatus.Available),
            Voucher(fuel.b.Slug, fuel.f.Id, fuel.n.Liters, VoucherStatus.Available),
            // An Assigned voucher is held against an order - the schema refuses one without it.
            Voucher(fuel.b.Slug, fuel.f.Id, fuel.n.Liters, VoucherStatus.Assigned, orderId));
        await context.SaveChangesAsync();

        var result = await new GetInventoryCommandHandler(context)
            .HandleAsync(new GetInventoryCommand(), CancellationToken.None);

        var item = result.Inventory.Should()
            .ContainSingle(i => i.FuelTypeId == fuel.f.Id && i.Liters == fuel.n.Liters)
            .Subject;
        item.Available.Should().Be(2);
        item.Assigned.Should().Be(1);
        item.Total.Should().Be(3);
        item.FuelTypeName.Should().Be(fuel.f.Name);
    }

    private static FuelVoucher Voucher(string brand, Guid fuelTypeId, decimal liters, VoucherStatus status, Guid? orderId = null) => new()
    {
        Id = Guid.NewGuid(),
        Brand = brand,
        BrandId = BrandIds.ForSlug(brand),
        FuelTypeId = fuelTypeId,
        Liters = liters,
        VoucherNumber = Guid.NewGuid().ToString("N"),
        QrPayload = Guid.NewGuid().ToString("N"),
        SupplierExpirationDate = new DateOnly(2027, 1, 1),
        CustomerExpirationDate = new DateOnly(2027, 1, 1),
        Status = status,
        OrderId = orderId,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };
}
