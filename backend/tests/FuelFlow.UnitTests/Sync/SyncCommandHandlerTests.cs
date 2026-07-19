using FluentAssertions;
using FuelFlow.Features.Orders.GetUserPurchases;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Sync.GetSync;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Sync;

public sealed class SyncCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public SyncCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task GetSync_ShouldReturnOrdersWithVouchersAndCounts()
    {
        var userId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var order1Id = Guid.NewGuid();
        var order2Id = Guid.NewGuid();

        var order1 = new Order
        {
            Id = order1Id,
            UserId = userId,
            ProductType = "OKKO A95 50L",
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 2,
            Price = 5000,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-4)
        };

        var order2 = new Order
        {
            Id = order2Id,
            UserId = userId,
            ProductType = "OKKO Diesel 50L",
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        var voucher1 = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var voucher2 = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
            VoucherNumber = "OKKO-2",
            QrPayload = "payload-2",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var voucher3 = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
            VoucherNumber = "OKKO-3",
            QrPayload = "payload-3",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var fulfillment1 = new Fulfillment
        {
            OrderId = order1Id,
            VoucherId = voucher1.Id,
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-4)
        };

        var fulfillment2 = new Fulfillment
        {
            OrderId = order1Id,
            VoucherId = voucher2.Id,
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-4)
        };

        var fulfillment3 = new Fulfillment
        {
            OrderId = order2Id,
            VoucherId = voucher3.Id,
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        _context.Orders.AddRange(order1, order2);
        _context.FuelVouchers.AddRange(voucher1, voucher2, voucher3);
        _context.Fulfillments.AddRange(fulfillment1, fulfillment2, fulfillment3);
        await _context.SaveChangesAsync();

        var getUserPurchasesHandler = new GetUserPurchasesCommandHandler(_context);
        var handler = new GetSyncCommandHandler(_context, getUserPurchasesHandler);
        var command = new GetSyncCommand(userId);

        var response = await handler.HandleAsync(command);

        response.TotalOrders.Should().Be(2);
        response.TotalVouchers.Should().Be(3);
        response.Orders.Should().HaveCount(2);

        var syncedOrder1 = response.Orders.First(o => o.Id == order1Id);
        syncedOrder1.Vouchers.Should().HaveCount(2);
        syncedOrder1.Vouchers.Should().Contain(v => v.Id == voucher1.Id);
        syncedOrder1.Vouchers.Should().Contain(v => v.Id == voucher2.Id);

        var syncedOrder2 = response.Orders.First(o => o.Id == order2Id);
        syncedOrder2.Vouchers.Should().HaveCount(1);
        syncedOrder2.Vouchers.Should().Contain(v => v.Id == voucher3.Id);
    }

    [Fact]
    public async Task GetSync_ShouldReturnEmptyResponse_WhenUserHasNoOrders()
    {
        var userId = Guid.NewGuid();

        var getUserPurchasesHandler = new GetUserPurchasesCommandHandler(_context);
        var handler = new GetSyncCommandHandler(_context, getUserPurchasesHandler);
        var command = new GetSyncCommand(userId);

        var response = await handler.HandleAsync(command);

        response.TotalOrders.Should().Be(0);
        response.TotalVouchers.Should().Be(0);
        response.Orders.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSync_ShouldReturnOrderWithoutVouchers_WhenOrderNotFulfilled()
    {
        var userId = Guid.Parse("b1234567-8abc-def0-1234-567890abcdef");
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            ProductType = "OKKO A95 50L",
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var getUserPurchasesHandler = new GetUserPurchasesCommandHandler(_context);
        var handler = new GetSyncCommandHandler(_context, getUserPurchasesHandler);
        var command = new GetSyncCommand(userId);

        var response = await handler.HandleAsync(command);

        response.TotalOrders.Should().Be(1);
        response.TotalVouchers.Should().Be(0);
        response.Orders.Should().HaveCount(1);
        response.Orders[0].Vouchers.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSync_ShouldOrderByCreatedAtDescending()
    {
        var userId = Guid.Parse("c2345678-9abc-def0-1234-567890abcdef");

        var oldOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductType = "OKKO A95 50L",
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10),
            FulfilledAtUtc = DateTime.UtcNow.AddDays(-9)
        };

        var newOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductType = "OKKO Diesel 50L",
            Provider = "OKKO",
            FuelTypeId = "okko-dp",
            Liters = 50,
            Quantity = 1,
            Price = 2500,
            Status = OrderStatus.Fulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            FulfilledAtUtc = DateTime.UtcNow
        };

        _context.Orders.AddRange(oldOrder, newOrder);
        await _context.SaveChangesAsync();

        var getUserPurchasesHandler = new GetUserPurchasesCommandHandler(_context);
        var handler = new GetSyncCommandHandler(_context, getUserPurchasesHandler);
        var command = new GetSyncCommand(userId);

        var response = await handler.HandleAsync(command);

        response.Orders.Should().HaveCount(2);
        response.Orders[0].Id.Should().Be(newOrder.Id);
        response.Orders[1].Id.Should().Be(oldOrder.Id);
    }
}

