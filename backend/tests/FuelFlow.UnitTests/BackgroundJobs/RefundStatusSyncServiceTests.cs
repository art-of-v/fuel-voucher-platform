using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

public sealed class RefundStatusSyncServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly RefundStatusSyncService _service;

    public RefundStatusSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // Mirror production: the API registers the context with a NoTracking
            // default, so write paths must opt in via AsTracking. Tests must catch
            // mutations lost to detached entities.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);

        _monobankClientMock = new Mock<IMonobankClient>();
        _service = new RefundStatusSyncService(
            _context,
            _monobankClientMock.Object,
            new NullLogger<RefundStatusSyncService>());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static Refund BuildRefund(Guid orderId, RefundStatus status = RefundStatus.Processing, DateTime? createdAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        UserId = Guid.NewGuid(),
        Amount = 52000,
        InvoiceId = "INV-REFUND-1",
        ExtRef = "idem-key-1",
        Status = status,
        IsAutomatic = false,
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Order BuildOrder(Guid id, OrderStatus status) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        Price = 5200,
        Status = status,
        MonobankInvoiceId = "INV-REFUND-1",
        IdempotencyKey = "idem-key-1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FuelVoucher BuildVoucher() => new()
    {
        Id = Guid.NewGuid(),
        Provider = "okko",
        FuelTypeId = "okko-95",
        Liters = 50,
        VoucherNumber = $"V-{Guid.NewGuid():N}",
        QrPayload = "q",
        ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Fulfillment BuildFulfillment(Guid orderId) => new()
    {
        Id = 1,
        OrderId = orderId,
        VoucherId = Guid.NewGuid(),
        FulfilledAtUtc = DateTime.UtcNow,
        Voucher = BuildVoucher()
    };

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldMarkCompleted_WhenCancelSucceeded()
    {
        var orderId = Guid.NewGuid();
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Completed);
        refund.MonobankStatus.Should().Be("success");
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldMarkFailed_WhenCancelFailed()
    {
        var orderId = Guid.NewGuid();
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "failure",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Failed);
        refund.MonobankStatus.Should().Be("failure");
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldLeaveProcessing_WhenStillProcessing()
    {
        var orderId = Guid.NewGuid();
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "processing",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Processing);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldUpdateOrderStatus_WhenCancelSucceeded()
    {
        var orderId = Guid.NewGuid();
        var order = BuildOrder(orderId, OrderStatus.PartiallyFulfilled);
        order.Fulfillments.Add(BuildFulfillment(orderId));
        _context.Orders.Add(order);
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "success",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyRefunded);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldRevertOrderStatus_WhenCancelFailed()
    {
        var orderId = Guid.NewGuid();
        var order = BuildOrder(orderId, OrderStatus.PartiallyRefunded);
        order.Fulfillments.Add(BuildFulfillment(orderId));
        _context.Orders.Add(order);
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "failure",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Failed);

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldRevertPrematureRefundedOrder_WhileRefundStillProcessing()
    {
        // Legacy rows written by the old premature-transition code: order already
        // PartiallyRefunded while the refund is still in flight. The sync job must
        // reconcile the order back without touching the refund.
        var orderId = Guid.NewGuid();
        var order = BuildOrder(orderId, OrderStatus.PartiallyRefunded);
        order.Fulfillments.Add(BuildFulfillment(orderId));
        _context.Orders.Add(order);
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = "INV-REFUND-1",
                Status = "success",
                CancelList = new List<MonobankCancelListItem>
                {
                    new()
                    {
                        Status = "processing",
                        Amount = 52000,
                        Ccy = 980,
                        ExtRef = "idem-key-1",
                        ModifiedDate = DateTime.UtcNow
                    }
                }
            });

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Processing);

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldTimeoutAndRevertOrder_WhenRefundOlderThan24h()
    {
        var orderId = Guid.NewGuid();
        var order = BuildOrder(orderId, OrderStatus.PartiallyRefunded);
        order.Fulfillments.Add(BuildFulfillment(orderId));
        _context.Orders.Add(order);
        _context.Refunds.Add(BuildRefund(orderId, createdAtUtc: DateTime.UtcNow.AddHours(-25)));
        await _context.SaveChangesAsync();

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Failed);
        refund.ErrorMessage.Should().Contain("Timed out");

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task SyncRefundForInvoiceAsync_ShouldMarkCompleted_FromWebhookCancelList()
    {
        var orderId = Guid.NewGuid();
        _context.Refunds.Add(BuildRefund(orderId));
        await _context.SaveChangesAsync();

        await _service.SyncRefundForInvoiceAsync(
            "INV-REFUND-1",
            new List<MonobankCancelListItem>
            {
                new()
                {
                    Status = "success",
                    Amount = 52000,
                    Ccy = 980,
                    ExtRef = "idem-key-1",
                    ModifiedDate = DateTime.UtcNow
                }
            });

        var refund = await _context.Refunds.SingleAsync();
        refund.Status.Should().Be(RefundStatus.Completed);
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
