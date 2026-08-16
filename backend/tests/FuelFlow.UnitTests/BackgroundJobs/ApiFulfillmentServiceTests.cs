using FluentAssertions;
using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

public sealed class ApiFulfillmentServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly TestableApiFulfillmentService _service;
    private readonly Mock<ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService>> _loggerMock;

    public ApiFulfillmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService>>();

        var monobankClientMock = new Mock<IMonobankClient>();
        monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });
        var refundHandler = new RefundOrderCommandHandler(
            _context,
            monobankClientMock.Object,
            new ProviderEventService(_context));

        _service = new TestableApiFulfillmentService(
            _context,
            _loggerMock.Object,
            refundHandler,
            new RuntimeSettingsService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private sealed class TestableApiFulfillmentService : FuelFlow.API.BackgroundJobs.FulfillmentService
    {
        private readonly ApplicationDbContext _db;

        public int TryMarkOrderFulfilledCalls { get; private set; }
        public int TryAssignVoucherCalls { get; private set; }

        public TestableApiFulfillmentService(
            ApplicationDbContext context,
            ILogger<FuelFlow.API.BackgroundJobs.FulfillmentService> logger,
            RefundOrderCommandHandler refundHandler,
            RuntimeSettingsService settings)
            : base(context, logger, refundHandler, settings)
        {
            _db = context;
        }

        protected internal override async Task<int> TryMarkOrderFulfilledAsync(Guid orderId, CancellationToken cancellationToken)
        {
            TryMarkOrderFulfilledCalls++;
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

        protected internal override async Task<int> TryAssignVoucherAsync(Guid voucherId, Guid userId, CancellationToken cancellationToken)
        {
            TryAssignVoucherCalls++;
            var voucher = await _db.FuelVouchers.FindAsync([voucherId], cancellationToken);
            if (voucher != null && voucher.Status == VoucherStatus.Available)
            {
                voucher.Status = VoucherStatus.Assigned;
                voucher.AssignedToUserId = userId;
                voucher.UpdatedAtUtc = DateTime.UtcNow;
                return await _db.SaveChangesAsync(cancellationToken);
            }
            return 0;
        }
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldAssignVoucherFulfillOrderAndPublishEvent()
    {
        var userId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
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
            Payload = System.Text.Json.JsonSerializer.Serialize(new OrderCreatedPayload
            {
                OrderId = order.Id,
                UserId = userId.ToString()
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
            .FirstOrDefaultAsync(f => f.OrderId == order.Id && f.VoucherId == voucher.Id);
        fulfillment.Should().NotBeNull();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();

        var fulfilledEvent = await _context.OutboxEvents
            .FirstOrDefaultAsync(e => e.EventType == OutboxEventType.OrderFulfilled);
        fulfilledEvent.Should().NotBeNull();
        fulfilledEvent!.Payload.Should().Contain(order.Id.ToString());
        fulfilledEvent.Processed.Should().BeFalse();

        _service.TryAssignVoucherCalls.Should().Be(1);
        _service.TryMarkOrderFulfilledCalls.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldPartiallyFulfillOrder_WhenNotEnoughVouchers()
    {
        var userId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 7500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
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
            Payload = System.Text.Json.JsonSerializer.Serialize(new OrderCreatedPayload
            {
                OrderId = order.Id,
                UserId = userId.ToString()
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
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        updatedOrder.FulfilledAtUtc.Should().BeNull();

        var fulfillments = await _context.Fulfillments
            .Where(f => f.OrderId == order.Id)
            .ToListAsync();
        fulfillments.Should().HaveCount(1);

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldBackfillOpenOrder_WhenNoOutboxEvents()
    {
        var userId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 2500,
            Status = OrderStatus.PendingFulfillment,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-2),
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
            VoucherNumber = "OKKO-BACKFILL",
            QrPayload = "backfill",
            Status = VoucherStatus.Available,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        _context.FuelVouchers.Add(voucher);
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
            .FirstOrDefaultAsync(f => f.OrderId == order.Id && f.VoucherId == voucher.Id);
        fulfillment.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessVoucherImportsAsync_ShouldMarkVoucherExpiredEventsAsProcessed()
    {
        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.VoucherExpired,
            Payload = System.Text.Json.JsonSerializer.Serialize(new { voucherId = Guid.NewGuid() }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherImportsAsync();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessVoucherImportsAsync_ShouldSkipAlreadyProcessedEvents()
    {
        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.VoucherExpired,
            Payload = "{}",
            Processed = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessVoucherImportsAsync();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldNotAutoRefund_WhenSettingDisabled()
    {
        var userId = Guid.NewGuid();
        var order = await SeedPartialOrderAsync(userId, "INV-DISABLED");

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        (await _context.Refunds.AnyAsync(r => r.OrderId == order.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldAutoRefund_WhenEnabledAndGracePeriodElapsed()
    {
        var userId = Guid.NewGuid();
        _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundEnabled, Value = "true", UpdatedAtUtc = DateTime.UtcNow });
        _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundDelayDays, Value = "0", UpdatedAtUtc = DateTime.UtcNow });
        var order = await SeedPartialOrderAsync(userId, "INV-ENABLED");

        await _service.ProcessPendingOrdersAsync();

        // The order keeps its fulfillment-derived status until Monobank confirms the
        // refund (RefundStatusSyncService transitions it to PartiallyRefunded then).
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);

        var refund = await _context.Refunds.SingleAsync(r => r.OrderId == order.Id);
        refund.Status.Should().Be(RefundStatus.Processing);
        refund.Amount.Should().Be(250000);
    }

    [Fact]
    public async Task ProcessPendingOrdersAsync_ShouldNotAutoRefund_WhenGracePeriodNotElapsed()
    {
        var userId = Guid.NewGuid();
        _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundEnabled, Value = "true", UpdatedAtUtc = DateTime.UtcNow });
        _context.AppSettings.Add(new AppSetting { Key = AppSettingKeys.AutoRefundDelayDays, Value = "30", UpdatedAtUtc = DateTime.UtcNow });
        var order = await SeedPartialOrderAsync(userId, "INV-GRACE");

        await _service.ProcessPendingOrdersAsync();

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
        (await _context.Refunds.AnyAsync(r => r.OrderId == order.Id)).Should().BeFalse();
    }

    private async Task<Order> SeedPartialOrderAsync(Guid userId, string monobankInvoiceId)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Price = 3 * 2500,
            Status = OrderStatus.PendingFulfillment,
            MonobankInvoiceId = monobankInvoiceId,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-2),
            LineItems = new List<OrderLineItem>
            {
                new OrderLineItem
                {
                    Provider = "OKKO",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 3,
                    UnitPrice = 2500,
                    LineTotal = 3 * 2500
                }
            }
        };

        for (var i = 0; i < 2; i++)
        {
            _context.FuelVouchers.Add(new FuelVoucher
            {
                Id = Guid.NewGuid(),
                Provider = "OKKO",
                FuelTypeId = "okko-95",
                Liters = 50,
                ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1),
                VoucherNumber = $"AR-{order.Id:N}-{i:D3}",
                QrPayload = $"payload-{order.Id:N}-{i:D3}",
                Status = VoucherStatus.Available,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }
}
