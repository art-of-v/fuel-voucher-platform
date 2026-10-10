using FluentAssertions;
using FuelFlow.Features.Orders.GetSavingsReport;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Orders;

public sealed class GetSavingsReportQueryHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly GetSavingsReportQueryHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public GetSavingsReportQueryHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _handler = new GetSavingsReportQueryHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>Adds a paid order with a single line. <paramref name="originalLineTotal"/> null = pre-capture row.</summary>
    private Order SeedOrder(
        OrderStatus status,
        decimal liters,
        int quantity,
        int lineTotal,
        int? originalLineTotal,
        Guid? userId = null,
        DateTime? createdAt = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _userId,
            Price = lineTotal,
            Status = status,
            CreatedAtUtc = createdAt ?? DateTime.UtcNow,
            UpdatedAtUtc = createdAt ?? DateTime.UtcNow
        };
        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Provider = "okko",
            FuelTypeId = "okko-dp",
            Liters = liters,
            Quantity = quantity,
            UnitPrice = lineTotal / quantity,
            LineTotal = lineTotal,
            OriginalLineTotal = originalLineTotal
        });
        _context.Orders.Add(order);
        return order;
    }

    private void SeedFulfilledVoucher(Order order, decimal liters, VoucherStatus status)
    {
        var voucherId = Guid.NewGuid();
        _context.FuelVouchers.Add(new FuelVoucher
        {
            Id = voucherId,
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = liters,
            ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"V-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            ImportJobId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        _context.Fulfillments.Add(new Fulfillment { OrderId = order.Id, VoucherId = voucherId, FulfilledAtUtc = DateTime.UtcNow });
    }

    [Fact]
    public async Task Report_AggregatesPaidTotalsLitersAndFrozenSavings()
    {
        // Bought 100 L for 2200, pump would have been 2500 → 300 saved.
        SeedOrder(OrderStatus.Fulfilled, liters: 100m, quantity: 1, lineTotal: 2200, originalLineTotal: 2500);
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.OrdersCount.Should().Be(1);
        report.TotalPaid.Should().Be(2200);
        report.TotalLiters.Should().Be(100m);
        report.TotalSavings.Should().Be(300);
    }

    [Fact]
    public async Task Report_PreCaptureRows_ContributeNoSavings_ButStillCountPaidAndLiters()
    {
        // Historical order predating the frozen column → no pump reference, 0 savings.
        SeedOrder(OrderStatus.Fulfilled, liters: 50m, quantity: 1, lineTotal: 1100, originalLineTotal: null);
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.TotalPaid.Should().Be(1100);
        report.TotalLiters.Should().Be(50m);
        report.TotalSavings.Should().Be(0);
    }

    [Fact]
    public async Task Report_ExcludesUnpaidCancelledAndRefundedOrders()
    {
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500);       // counts
        SeedOrder(OrderStatus.PendingPayment, 100m, 1, 2200, 2500);  // never paid
        SeedOrder(OrderStatus.Cancelled, 100m, 1, 2200, 2500);       // unwound
        SeedOrder(OrderStatus.Refunded, 100m, 1, 2200, 2500);        // reversed
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.OrdersCount.Should().Be(1);
        report.TotalPaid.Should().Be(2200);
        report.TotalSavings.Should().Be(300);
    }

    [Fact]
    public async Task Report_SaleAbovePump_ClampsSavingToZero()
    {
        // Defensive: pump lower than paid should never inflate into a negative "saving".
        SeedOrder(OrderStatus.Fulfilled, liters: 100m, quantity: 1, lineTotal: 2200, originalLineTotal: 2000);
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.TotalSavings.Should().Be(0);
    }

    [Fact]
    public async Task Report_RemainingCountsOnlyOwnedUnredeemedVouchers()
    {
        var order = SeedOrder(OrderStatus.Fulfilled, liters: 300m, quantity: 3, lineTotal: 6600, originalLineTotal: 7500);
        SeedFulfilledVoucher(order, 100m, VoucherStatus.Assigned);    // owned, unredeemed
        SeedFulfilledVoucher(order, 100m, VoucherStatus.Used);        // redeemed → gone
        SeedFulfilledVoucher(order, 100m, VoucherStatus.Assigned);    // owned, unredeemed
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.RemainingVouchers.Should().Be(2);
        report.RemainingLiters.Should().Be(200m);
    }

    [Fact]
    public async Task Report_ScopedToUser_IgnoresOtherCustomersOrders()
    {
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500);                          // mine
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500, userId: Guid.NewGuid());  // someone else
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.OrdersCount.Should().Be(1);
        report.TotalPaid.Should().Be(2200);
    }

    [Fact]
    public async Task Report_NoOrders_ReturnsZeros()
    {
        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.OrdersCount.Should().Be(0);
        report.TotalPaid.Should().Be(0);
        report.TotalLiters.Should().Be(0m);
        report.TotalSavings.Should().Be(0);
        report.RemainingVouchers.Should().Be(0);
        report.RemainingLiters.Should().Be(0m);
        report.Monthly.Should().BeEmpty();
    }

    [Fact]
    public async Task Report_FiltersOrdersByCreatedAtPeriod()
    {
        var march = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var june = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500, createdAt: march); // outside
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 3300, 3600, createdAt: june);  // inside
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(
            _userId,
            FromDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ToDate: new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)));

        report.OrdersCount.Should().Be(1);
        report.TotalPaid.Should().Be(3300);
        report.TotalSavings.Should().Be(300);
    }

    [Fact]
    public async Task Report_MonthlyBreakdown_GroupsByOrderMonthOldestFirst()
    {
        var march = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var june = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc);
        SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500, createdAt: june);  // 300 saved
        SeedOrder(OrderStatus.Fulfilled, 50m, 1, 1100, 1250, createdAt: march);  // 150 saved
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(_userId));

        report.Monthly.Should().HaveCount(2);
        report.Monthly[0].Month.Should().Be("2026-03");
        report.Monthly[0].Paid.Should().Be(1100);
        report.Monthly[0].Saved.Should().Be(150);
        report.Monthly[0].Liters.Should().Be(50m);
        report.Monthly[1].Month.Should().Be("2026-06");
        report.Monthly[1].Paid.Should().Be(2200);
        report.Monthly[1].Saved.Should().Be(300);
        report.Monthly[1].Liters.Should().Be(100m);
    }

    [Fact]
    public async Task Report_RemainingBalance_IgnoresPeriodFilter()
    {
        // Order predates the period window, but the vouchers are still owned today, so the
        // "now" remaining snapshot must still count them even though the period excludes the order.
        var march = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var order = SeedOrder(OrderStatus.Fulfilled, 100m, 1, 2200, 2500, createdAt: march);
        SeedFulfilledVoucher(order, 100m, VoucherStatus.Assigned);
        await _context.SaveChangesAsync();

        var report = await _handler.HandleAsync(new GetSavingsReportQuery(
            _userId,
            FromDate: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ToDate: new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)));

        report.OrdersCount.Should().Be(0);          // period excludes the March order
        report.RemainingVouchers.Should().Be(1);    // but the balance is a current snapshot
        report.RemainingLiters.Should().Be(100m);
    }
}
