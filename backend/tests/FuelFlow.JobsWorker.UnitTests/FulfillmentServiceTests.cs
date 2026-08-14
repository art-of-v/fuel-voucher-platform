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

public sealed class FulfillmentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FulfillmentService _service;
    private readonly Mock<ILogger<FulfillmentService>> _loggerMock;

    public FulfillmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<FulfillmentService>>();
        _service = new TestableFulfillmentService(_context, _loggerMock.Object);
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
    public async Task ProcessPendingOrders_ShouldAssignSingleVoucher_WhenQuantityIsOne()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = "OKKO-12345",
            QrPayload = "test-payload",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.Add(voucher);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder.Should().NotBeNull();
        updatedOrder!.Status.Should().Be(OrderStatus.Fulfilled);
        updatedOrder.FulfilledAtUtc.Should().NotBeNull();

        var updatedVoucher = await _context.FuelVouchers.FindAsync(voucher.Id);
        updatedVoucher.Should().NotBeNull();
        updatedVoucher!.Status.Should().Be(VoucherStatus.Assigned);
        updatedVoucher.AssignedToUserId.Should().Be(userId);

        var fulfillment = await _context.Fulfillments
            .FirstOrDefaultAsync(f => f.OrderId == orderId && f.VoucherId == voucher.Id);
        fulfillment.Should().NotBeNull();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldAssignMultipleVouchers_WhenQuantityIsGreaterThanOne()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 7500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 3,
                    UnitPrice = 2500,
                    LineTotal = 7500
                }
            }
        };

        var vouchers = new List<FuelVoucher>
        {
            new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 50,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = "OKKO-1",
                QrPayload = "payload-1",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 50,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2),
                VoucherNumber = "OKKO-2",
                QrPayload = "payload-2",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 50,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3),
                VoucherNumber = "OKKO-3",
                QrPayload = "payload-3",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            }
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.AddRange(vouchers);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Fulfilled);

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(3);

        var assignedVouchers = await _context.FuelVouchers
            .Where(v => v.Status == VoucherStatus.Assigned && v.AssignedToUserId == userId)
            .ToListAsync();
        assignedVouchers.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldAssignVouchersWithNearestExpirationFirst()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 5000,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 2,
                    UnitPrice = 2500,
                    LineTotal = 5000
                }
            }
        };

        var expiringVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            VoucherNumber = "OKKO-EXPIRING",
            QrPayload = "expiring",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var freshVoucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6),
            VoucherNumber = "OKKO-FRESH",
            QrPayload = "fresh",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.AddRange(freshVoucher, expiringVoucher);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == orderId)
            .OrderBy(f => f.FulfilledAtUtc)
            .ToListAsync();

        fulfillments.Should().HaveCount(2);
        fulfillments[0].VoucherId.Should().Be(expiringVoucher.Id);
        fulfillments[1].VoucherId.Should().Be(freshVoucher.Id);
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldPartiallyFulfillOrder_WhenNotEnoughVouchers()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 7500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 3,
                    UnitPrice = 2500,
                    LineTotal = 7500
                }
            }
        };

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = "OKKO-1",
            QrPayload = "payload-1",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.Add(voucher);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        updatedOrder.FulfilledAtUtc.Should().BeNull();

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().HaveCount(1);

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldNotProcessAlreadyFulfilledOrder()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.Fulfilled,
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().BeEmpty();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldNotProcessCancelledOrder()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.Cancelled,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == orderId)
            .ToListAsync();
        fulfillments.Should().BeEmpty();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldPublishOrderFulfilledEvent_WhenFullyFulfilled()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var order = new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };

        var voucher = new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
            VoucherNumber = "OKKO-12345",
            QrPayload = "test-payload",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                userId = order.UserId
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.Add(voucher);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var fulfilledEvent = await _context.OutboxEvents
            .FirstOrDefaultAsync(e => e.EventType == OutboxEventType.OrderFulfilled && e.Payload.Contains(orderId.ToString()));

        fulfilledEvent.Should().NotBeNull();
        fulfilledEvent!.Processed.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessPendingOrders_ShouldProcessMultipleEvents_InFifoOrder()
    {
        var order1 = CreateTestOrder(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10));
        var order2 = CreateTestOrder(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-5));
        var order3 = CreateTestOrder(Guid.NewGuid(), DateTime.UtcNow);

        var vouchers = Enumerable.Range(1, 3).Select(i => CreateTestVoucher(i)).ToList();

        var event1 = CreateOutboxEvent(order1.Id, order1.UserId, DateTime.UtcNow.AddMinutes(-10));
        var event2 = CreateOutboxEvent(order2.Id, order2.UserId, DateTime.UtcNow.AddMinutes(-5));
        var event3 = CreateOutboxEvent(order3.Id, order3.UserId, DateTime.UtcNow);

        _context.Orders.AddRange(order1, order2, order3);
        _context.FuelVouchers.AddRange(vouchers);
        _context.OutboxEvents.AddRange(event1, event2, event3);
        await _context.SaveChangesAsync();

        await _service.ProcessPendingOrdersAsync();

        var processedEvents = await _context.OutboxEvents
            .Where(e => e.Processed)
            .OrderBy(e => e.ProcessedAtUtc)
            .ToListAsync();

        processedEvents.Should().HaveCount(3);
        processedEvents[0].Id.Should().Be(event1.Id);
        processedEvents[1].Id.Should().Be(event2.Id);
        processedEvents[2].Id.Should().Be(event3.Id);
    }

    private Order CreateTestOrder(Guid userId, DateTime createdAt)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = createdAt,
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };
    }

    private FuelVoucher CreateTestVoucher(int index)
    {
        return new FuelVoucher
        {
            Id = Guid.NewGuid(),
            Provider = "OKKO",
            FuelTypeId = "okko-95",
            Liters = 50,
            ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(index),
            VoucherNumber = $"OKKO-{index}",
            QrPayload = $"payload-{index}",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private OutboxEvent CreateOutboxEvent(Guid orderId, Guid userId, DateTime createdAt)
    {
        return new OutboxEvent
        {
            EventType = OutboxEventType.OrderCreated,
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId,
                userId,
                provider = "OKKO",
                FuelTypeId = "okko-95",
                liters = 50,
                quantity = 1
            }),
            Processed = false,
            CreatedAtUtc = createdAt
        };
    }
}
