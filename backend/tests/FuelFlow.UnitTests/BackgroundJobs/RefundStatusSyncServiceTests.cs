using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.BackgroundJobs;

public sealed class RefundStatusSyncServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly StubMonobankClientFactory _monobankClientFactory;
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

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientFactory = new StubMonobankClientFactory(_monobankClientMock.Object);
        _service = new RefundStatusSyncService(
            _context,
            _monobankClientFactory,
            new NullLogger<RefundStatusSyncService>());
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// Seeds the capture a refund will reverse, and returns it. A refund names the capture it
    /// reverses, so the parent has to exist first - the old <c>refunds</c> row needed no parent and
    /// these tests never had to think about one.
    /// </summary>
    private Payment SeedCapture(ApplicationDbContext ctx, Guid orderId)
    {
        var capture = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Kind = PaymentKind.Capture,
            Provider = "monobank",
            ProviderInvoiceId = "INV-REFUND-1",
            AmountUah = 520.00m,
            AmountKopecks = 52000,
            Currency = "UAH",
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        ctx.Payments.Add(capture);
        return capture;
    }

    /// <summary>
    /// A pending refund, as a payment of kind 'Refund'. The capture it reverses comes from
    /// <see cref="SeedCapture"/>, because a refund has to name what it gives back.
    /// </summary>
    private static Payment BuildRefund(
        Guid orderId,
        Guid captureId,
        PaymentStatus status = PaymentStatus.Pending,
        DateTime? createdAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        Kind = PaymentKind.Refund,
        ParentPaymentId = captureId,
        Provider = "monobank",
        ProviderInvoiceId = "INV-REFUND-1",
        AmountUah = 520.00m,
        AmountKopecks = 52000,
        Currency = "UAH",
        Status = status,
        IsAutomatic = false,
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static Order BuildOrder(
        Guid id, OrderStatus status, MonobankMerchant? merchant = MonobankMerchant.Live) => new()
    {
        Id = id,
        UserId = Guid.NewGuid(),
        Price = 5200,
        Status = status,
        MonobankInvoiceId = "INV-REFUND-1",
        MonobankMerchant = merchant,
        IdempotencyKey = "idem-key-1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static FuelVoucher BuildVoucher() => new()
    {
        Id = Guid.NewGuid(),
        Brand = "okko",
        BrandId = BrandIds.ForSlug("okko"),
        FuelTypeId = ToUuid("okko-95"),
        Liters = 50,
        VoucherNumber = $"V-{Guid.NewGuid():N}",
        QrPayload = "q",
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
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
    public async Task SyncPendingRefundsAsync_ShouldPollTheMerchantPersistedOnOrder()
    {
        // A refund in flight must be checked against the merchant that actually issued the
        // invoice, which is the one recorded at checkout - not the live token by default.
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded, MonobankMerchant.Sandbox));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus { InvoiceId = "INV-REFUND-1", Status = "success" });

        await _service.SyncPendingRefundsAsync();

        _monobankClientFactory.RequestedMerchants.Should().Equal(MonobankMerchant.Sandbox);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_OrderWithoutMerchantRecorded_DefaultsToLive()
    {
        // Refunds created before the two-merchant change have no merchant on the order. Defaulting
        // to live keeps them resolvable; defaulting to sandbox would strand real money.
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded, merchant: null));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-REFUND-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus { InvoiceId = "INV-REFUND-1", Status = "success" });

        await _service.SyncPendingRefundsAsync();

        _monobankClientFactory.RequestedMerchants.Should().Equal(MonobankMerchant.Live);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldMarkCompleted_WhenCancelSucceeded()
    {
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Succeeded);
        // The brand's word is no longer stored on the refund; the typed status is the record.
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldMarkFailed_WhenCancelFailed()
    {
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Failed);
        // Same: the typed Failed status above is the record.
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldLeaveProcessing_WhenStillProcessing()
    {
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task SyncPendingRefundsAsync_ShouldUpdateOrderStatus_WhenCancelSucceeded()
    {
        var orderId = Guid.NewGuid();
        var order = BuildOrder(orderId, OrderStatus.PartiallyFulfilled);
        order.Fulfillments.Add(BuildFulfillment(orderId));
        _context.Orders.Add(order);
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Failed);

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
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Pending);

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
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id, createdAtUtc: DateTime.UtcNow.AddHours(-25)));
        await _context.SaveChangesAsync();

        await _service.SyncPendingRefundsAsync();

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Failed);
        refund.ErrorMessage.Should().Contain("Timed out");

        var updatedOrder = await _context.Orders.FindAsync(orderId);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task SyncRefundForInvoiceAsync_ShouldMarkCompleted_FromWebhookCancelList()
    {
        var orderId = Guid.NewGuid();
        _context.Orders.Add(BuildOrder(orderId, OrderStatus.PartiallyRefunded));
        var capture = SeedCapture(_context, orderId);
        _context.Payments.Add(BuildRefund(orderId, capture.Id));
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

        var refund = await _context.Payments.SingleAsync(p => p.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Succeeded);
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
