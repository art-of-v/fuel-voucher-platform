using FluentAssertions;
using FuelFlow.Features.Admin.GetDashboard;
using FuelFlow.Features.Admin.GetReconciliation;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Admin;

public sealed class AdminQueryHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public AdminQueryHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        SeedFuelPackages();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private void SeedFuelPackages()
    {
        _context.FuelPackages.AddRange(
            CreateFuelPackage("okko-95-50", "okko", "okko-95", "A-95", 50m, 2.0m),
            CreateFuelPackage("wog-dp-20", "wog", "wog-dp", "ДП Mustang", 20m, 2.0m));
        _context.SaveChanges();
    }

    private static FuelPackage CreateFuelPackage(string id, string stationId, string fuelTypeId, string fuelName, decimal liters, decimal marginUahPerLiter)
    {
        return new FuelPackage
        {
            Id = id,
            StationId = stationId,
            FuelTypeId = fuelTypeId,
            FuelName = fuelName,
            Liters = liters,
            Price = 1000,
            OriginalPrice = 800,
            MarginUahPerLiter = marginUahPerLiter,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static FuelVoucher CreateVoucher(string provider, string fuelTypeId, VoucherStatus status)
    {
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = $"{provider}-{Guid.NewGuid().ToString()[..8]}",
            QrPayload = Guid.NewGuid().ToString(),
            Status = status,
            AssignedToUserId = status == VoucherStatus.Assigned || status == VoucherStatus.Used ? UserId : null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static Order CreateOrder(Guid id, Guid userId, OrderStatus status, MonobankStatus? monobankStatus, DateTime createdAtUtc)
    {
        return new Order
        {
            Id = id,
            UserId = userId,
            Price = 1000,
            Status = status,
            MonobankStatus = monobankStatus,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static void AddLineItem(Order order, string provider, string fuelTypeId, decimal liters, int quantity, int unitPrice)
    {
        order.LineItems.Add(new OrderLineItem
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            FuelTypeId = fuelTypeId,
            Liters = liters,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity
        });
    }

    private static Refund CreateRefund(Guid orderId, int amountKopecks) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        UserId = UserId,
        Amount = amountKopecks,
        InvoiceId = "INV-TEST",
        ExtRef = $"ext-{orderId}",
        Status = RefundStatus.Completed,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task GetDashboard_ShouldReturnCounts()
    {
        _context.Users.AddRange(
            new User { Id = UserId, PhoneNumber = "+380991111111", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new User { Id = OtherUserId, PhoneNumber = "+380992222222", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        _context.SaveChanges();

        _context.FuelVouchers.AddRange(
            CreateVoucher("OKKO", "okko-95", VoucherStatus.Available),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.Imported),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.Assigned),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.Used),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.VerificationFailed),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.VerifiedWithWarnings),
            CreateVoucher("OKKO", "okko-95", VoucherStatus.Expired));
        _context.SaveChanges();

        var fulfilledOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.Fulfilled, MonobankStatus.Success, DateTime.UtcNow.AddDays(-3));
        AddLineItem(fulfilledOrder, "okko", "okko-95", 50m, 1, 1000);

        var partialOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.PartiallyFulfilled, MonobankStatus.Success, DateTime.UtcNow.AddDays(-2));
        AddLineItem(partialOrder, "wog", "wog-dp", 20m, 2, 1000);

        var pendingOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.PendingFulfillment, MonobankStatus.Pending, DateTime.UtcNow.AddDays(-1));

        _context.Orders.AddRange(fulfilledOrder, partialOrder, pendingOrder);
        _context.SaveChanges();

        var handler = new GetDashboardQueryHandler(_context);
        var query = new GetDashboardQuery();

        var response = await handler.HandleAsync(query);

        response.Users.Total.Should().Be(2);

        response.Vouchers.Total.Should().Be(7);
        response.Vouchers.Available.Should().Be(2);
        response.Vouchers.Assigned.Should().Be(1);
        response.Vouchers.Used.Should().Be(1);
        response.Vouchers.VerificationFailed.Should().Be(1);
        response.Vouchers.VerifiedWithWarnings.Should().Be(1);
        response.Vouchers.ByProvider.Should().ContainSingle(v => v.Provider == "OKKO" && v.Count == 7);

        response.Orders.Total.Should().Be(3);
        response.Orders.Pending.Should().Be(2);
        response.Orders.Fulfilled.Should().Be(1);
        response.Orders.RevenueUah.Should().Be(180);
    }

    [Fact]
    public async Task GetDashboard_ShouldReturnEmptyStats_WhenNoData()
    {
        var handler = new GetDashboardQueryHandler(_context);
        var query = new GetDashboardQuery();

        var response = await handler.HandleAsync(query);

        response.Users.Total.Should().Be(0);
        response.Vouchers.Total.Should().Be(0);
        response.Vouchers.Available.Should().Be(0);
        response.Vouchers.Assigned.Should().Be(0);
        response.Vouchers.Used.Should().Be(0);
        response.Vouchers.VerificationFailed.Should().Be(0);
        response.Vouchers.VerifiedWithWarnings.Should().Be(0);
        response.Vouchers.ByProvider.Should().BeEmpty();
        response.Orders.Total.Should().Be(0);
        response.Orders.Pending.Should().Be(0);
        response.Orders.Fulfilled.Should().Be(0);
        response.Orders.RevenueUah.Should().Be(0);
    }

    [Fact]
    public async Task GetReconciliation_ShouldReturnMatchingSummary_WhenSeeded()
    {
        var voucher = CreateVoucher("OKKO", "okko-95", VoucherStatus.Assigned);
        var availableVoucher = CreateVoucher("OKKO", "okko-95", VoucherStatus.Available);
        _context.FuelVouchers.AddRange(voucher, availableVoucher);

        var fulfilledOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.Fulfilled, MonobankStatus.Success, DateTime.UtcNow.AddDays(-2));
        AddLineItem(fulfilledOrder, "okko", "okko-95", 50m, 1, 1000);
        fulfilledOrder.Fulfillments.Add(new Fulfillment
        {
            VoucherId = voucher.Id,
            FulfilledAtUtc = DateTime.UtcNow
        });

        var pendingOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.PendingFulfillment, null, DateTime.UtcNow.AddDays(-1));
        AddLineItem(pendingOrder, "wog", "wog-dp", 20m, 1, 1000);

        _context.Orders.AddRange(fulfilledOrder, pendingOrder);
        _context.OutboxEvents.Add(new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = "{}",
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        _context.SaveChanges();

        var handler = new GetReconciliationQueryHandler(_context);
        var query = new GetReconciliationQuery();

        var response = await handler.HandleAsync(query);

        response.Should().NotBeNull();
        response.Summary.TotalOrders.Should().Be(2);
        response.Summary.Fulfilled.Should().Be(1);
        response.Summary.PaidUnfulfilled.Should().Be(1);
        response.Summary.OrphanVouchers.Should().Be(0);
        response.Summary.UnprocessedEvents.Should().Be(1);
        response.Summary.LowInventoryProviders.Should().Be(1);

        // Ledger fields: only the Success-paid order counts as received; its
        // delivered voucher carries the whole order value; nothing refunded.
        response.Summary.RefundedOrders.Should().Be(0);
        response.Summary.TotalReceivedKopecks.Should().Be(100000);
        response.Summary.TotalFulfilledValueKopecks.Should().Be(100000);
        response.Summary.TotalRefundedKopecks.Should().Be(0);
        // Margin earned on the one delivered voucher: 2 UAH/L * 50 L * 1.
        response.Summary.TotalRevenueKopecks.Should().Be(100);

        response.ThreeWayMatch.Should().HaveCount(2);
        response.ThreeWayMatch.Should().ContainSingle(t => t.OrderId == fulfilledOrder.Id && t.MatchStatus == "OK");
        response.ThreeWayMatch.Should().ContainSingle(t => t.OrderId == pendingOrder.Id && t.MatchStatus == "UNFULFILLED");

        response.Exceptions.Should().Contain(e => e.Type == "UNFULFILLED");

        response.VoucherFunnel.Should().Contain(f => f.Status == nameof(VoucherStatus.Assigned) && f.Count == 1);
        response.VoucherFunnel.Should().Contain(f => f.Status == nameof(VoucherStatus.Available) && f.Count == 1);

        response.RevenueSummary.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetReconciliation_ShouldComputeLedgerFields_WhenOrdersAreRefunded()
    {
        var voucher = CreateVoucher("OKKO", "okko-95", VoucherStatus.Assigned);
        _context.FuelVouchers.Add(voucher);

        // Fully refunded: nothing delivered, whole amount returned.
        var refundedOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.Refunded, MonobankStatus.Success, DateTime.UtcNow.AddDays(-3));
        AddLineItem(refundedOrder, "okko", "okko-95", 50m, 1, 1000);

        // Half fulfilled: one of two vouchers delivered, the rest refunded.
        var partialOrder = CreateOrder(Guid.NewGuid(), UserId, OrderStatus.PartiallyRefunded, MonobankStatus.Success, DateTime.UtcNow.AddDays(-2));
        AddLineItem(partialOrder, "okko", "okko-95", 50m, 2, 1000);
        partialOrder.Fulfillments.Add(new Fulfillment
        {
            VoucherId = voucher.Id,
            FulfilledAtUtc = DateTime.UtcNow
        });

        _context.Orders.AddRange(refundedOrder, partialOrder);
        _context.Refunds.AddRange(
            CreateRefund(refundedOrder.Id, 100000),
            CreateRefund(partialOrder.Id, 100000));
        _context.SaveChanges();

        var handler = new GetReconciliationQueryHandler(_context);
        var query = new GetReconciliationQuery();

        var response = await handler.HandleAsync(query);

        response.Summary.RefundedOrders.Should().Be(2);
        response.Summary.TotalReceivedKopecks.Should().Be(200000);
        response.Summary.TotalFulfilledValueKopecks.Should().Be(100000);
        response.Summary.TotalRefundedKopecks.Should().Be(200000);
        // Margin is earned only on the delivered voucher: 2 UAH/L * 50 L * 1.
        response.Summary.TotalRevenueKopecks.Should().Be(100);

        response.ThreeWayMatch.Should().ContainSingle(t =>
            t.OrderId == refundedOrder.Id
            && t.MatchStatus == "REFUNDED"
            && t.RefundStatus == nameof(RefundStatus.Completed)
            && t.RefundedKopecks == 100000);
        response.ThreeWayMatch.Should().ContainSingle(t =>
            t.OrderId == partialOrder.Id
            && t.MatchStatus == "PARTIAL_REFUNDED"
            && t.RefundStatus == nameof(RefundStatus.Completed)
            && t.RefundedKopecks == 100000
            && t.VouchersDelivered == 1
            && t.VouchersExpected == 2);

        response.RevenueSummary.Should().ContainSingle(m =>
            m.OrderCount == 2 && m.RevenueKopecks == 100);
    }

    [Fact]
    public async Task GetReconciliation_ShouldReturnEmptyResult_WhenNoData()
    {
        var handler = new GetReconciliationQueryHandler(_context);
        var query = new GetReconciliationQuery();

        var response = await handler.HandleAsync(query);

        response.Should().NotBeNull();
        response.Summary.TotalOrders.Should().Be(0);
        response.ThreeWayMatch.Should().BeEmpty();
        response.Exceptions.Should().BeEmpty();
        response.VoucherFunnel.Should().BeEmpty();
        response.RevenueSummary.Should().BeEmpty();
    }
}
