using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.JobsWorker.Services;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.JobsWorker.UnitTests;

public sealed class FulfillmentServicePartialBackfillTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FulfillmentService _service;

    public FulfillmentServicePartialBackfillTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new TestableFulfillmentService(_context, new Mock<ILogger<FulfillmentService>>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private sealed class TestableFulfillmentService : FulfillmentService
    {
        private readonly ApplicationDbContext _db;

        public TestableFulfillmentService(ApplicationDbContext context, ILogger<FulfillmentService> logger) : base(context, logger)
        {
            _db = context;
        }

        protected override async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var order = await _db.Orders.FindAsync([orderId], cancellationToken);
            if (order != null && (order.Status == OrderStatus.PendingFulfillment || order.Status == OrderStatus.PartiallyFulfilled))
            {
                order.Status = OrderStatus.Fulfilled;
                order.FulfilledAtUtc = DateTime.UtcNow;
                order.UpdatedAtUtc = DateTime.UtcNow;
                return await _db.SaveChangesAsync(cancellationToken);
            }
            return 0;
        }

        protected override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, Guid? legalEntityId, CancellationToken cancellationToken)
        {
            var voucher = await _db.FuelVouchers.FindAsync([voucherId], cancellationToken);
            if (voucher != null && voucher.Status == VoucherStatus.Available)
            {
                voucher.Status = VoucherStatus.Assigned;
                voucher.AssignedToUserId = userId;
                voucher.LegalEntityId = legalEntityId;
                voucher.WorkerUserId = null;
                voucher.UpdatedAtUtc = DateTime.UtcNow;
                return await _db.SaveChangesAsync(cancellationToken);
            }
            return 0;
        }
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldBackfillPartiallyFulfilledOrder_WhenNewVoucherBecomesAvailable()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2000,
            Status = OrderStatus.PartiallyFulfilled,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 20,
                    Quantity = 2,
                    UnitPrice = 1000,
                    LineTotal = 2000
                }
            }
        };

        var existingAssignedVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 20,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = "OKKO-ASSIGNED-1",
            QrPayload = "assigned-1",
            Status = VoucherStatus.Assigned,
            AssignedToUserId = userId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTime.UtcNow
        };

        var existingFulfillment = new Fulfillment
        {
            OrderId = orderId,
            VoucherId = existingAssignedVoucher.Id,
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        var newAvailableVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 20,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2),
            VoucherNumber = "OKKO-AVAILABLE-2",
            QrPayload = "available-2",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.AddRange(existingAssignedVoucher, newAvailableVoucher);
        _context.Fulfillments.Add(existingFulfillment);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder.Should().NotBeNull();
        updatedOrder!.Status.Should().Be(OrderStatus.Fulfilled);
        updatedOrder.FulfilledAtUtc.Should().NotBeNull();

        var totalFulfillments = await _context.Fulfillments.CountAsync(f => f.OrderId == orderId);
        totalFulfillments.Should().Be(2);

        var updatedNewVoucher = await _context.FuelVouchers.FindAsync(newAvailableVoucher.Id);
        updatedNewVoucher.Should().NotBeNull();
        updatedNewVoucher!.Status.Should().Be(VoucherStatus.Assigned);
        updatedNewVoucher.AssignedToUserId.Should().Be(userId);
    }
}
