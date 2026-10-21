using FluentAssertions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Vouchers;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Moq;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.UnitTests.Orders;

public sealed class RefundOrderCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly StubMonobankClientFactory _monobankClientFactory;
    private readonly RefundOrderCommandHandler _handler;

    public RefundOrderCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // Stricter than production on purpose: DatabaseSetup registers TrackAll, and a write that
            // only lands because the identity map happened to resolve a second load of the same row is
            // one DI change away from being silently dropped. Running NoTracking keeps the retry path
            // honest about mutating a tracked instance.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        _monobankClientFactory = new StubMonobankClientFactory(_monobankClientMock.Object);
        _handler = new RefundOrderCommandHandler(
            _context,
            _monobankClientFactory,
            new AuditEventService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    /// <summary>
    /// Seeds a refund for this order: the capture it reverses, then the refund payment.
    /// </summary>
    /// <remarks>
    /// A refund names the capture it reverses, so the parent has to exist first - the old
    /// <c>refunds</c> row needed no parent and these tests never had to think about one. The capture is
    /// succeeded so the handler's capture lookup finds it; the refund's own status is whatever the test
    /// is arranging.
    /// </remarks>
    private void SeedRefundPayment(Order order, PaymentStatus status)
    {
        var capture = new Payment
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Kind = PaymentKind.Capture,
        Provider = "monobank",
        ProviderInvoiceId = order.MonobankInvoiceId ?? "INV-DEFAULT",
        AmountUah = order.Price,
        AmountKopecks = FuelFlow.SharedKernel.Money.ToKopecks(order.Price),
        Currency = "UAH",
        Status = PaymentStatus.Succeeded,
        PaidAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
    _context.Payments.Add(capture);

    _context.Payments.Add(new Payment
    {
        Id = Guid.NewGuid(),
        OrderId = order.Id,
        Kind = PaymentKind.Refund,
        ParentPaymentId = capture.Id,
        Provider = "monobank",
        ProviderInvoiceId = capture.ProviderInvoiceId,
        AmountUah = order.Price,
        AmountKopecks = capture.AmountKopecks,
        Currency = "UAH",
        Status = status,
        PaidAtUtc = status == PaymentStatus.Succeeded ? DateTime.UtcNow : null,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    });
}

    /// <summary>
    /// A PAID order by default: every suite below is about returning money a customer actually
    /// handed over, and the fixture now says so explicitly.
    /// </summary>
    /// <remarks>
    /// It used to leave <c>MonobankStatus</c> unset and still be treated as refundable, which
    /// only worked because the amount was derived from line items alone. That made the fixture
    /// quietly assert the thing this PR removes — that an order with an invoice is refundable
    /// whether or not anyone paid. Override <paramref name="monobankStatus"/> for the unpaid cases.
    /// </remarks>
    private static Order BuildOrder(
        OrderStatus status = OrderStatus.PartiallyFulfilled,
        MonobankStatus? monobankStatus = MonobankStatus.Success) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Price = 5000,
        Status = status,
        MonobankInvoiceId = "INV123",
        MonobankStatus = monobankStatus,
        IdempotencyKey = "idem-key-1",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        LineItems =
        {
            new OrderLineItem
            {
                Id = Guid.NewGuid(),
                BrandId = BrandIds.ForSlug("okko"),
                Brand = "okko",
                FuelTypeId = ToUuid("okko-95"),
                Liters = 50,
                Quantity = 2,
                UnitPrice = 2500,
                LineTotal = 5000
            }
        }
    };

    private static FuelVoucher BuildVoucher(string brand, string fuelTypeId, decimal liters) => new()
    {
        Id = Guid.NewGuid(),
        Brand = brand,
        BrandId = BrandIds.ForSlug(brand),
        FuelTypeId = ToUuid(fuelTypeId),
        Liters = liters,
        VoucherNumber = $"V-{Guid.NewGuid():N}",
        QrPayload = "q",
        SupplierExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        CustomerExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task HandleAsync_ShouldCreateRefund_WithServerComputedAmount()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(250000);

        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var refund = await _context.Payments.SingleAsync(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund);
        refund.AmountKopecks.Should().Be(250000);
        refund.ProviderInvoiceId.Should().Be("INV123");
        refund.IsAutomatic.Should().BeFalse();
        refund.Status.Should().Be(PaymentStatus.Pending);

        // Order status must stay fulfillment-derived until Monobank confirms the refund,
        // otherwise the admin shows "PartiallyRefunded + Refund pending" at the same time.
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefundThroughMerchantPersistedOnOrder()
    {
        // The refund must hit the merchant that took the money - not whatever is configured as the
        // live token today. A sandbox order refunded on the live merchant would move real money for
        // a test payment (or fail outright); a live order refunded on the sandbox refunds nothing.
        var order = BuildOrder();
        order.MonobankMerchant = MonobankMerchant.Sandbox;
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        _monobankClientFactory.RequestedMerchants.Should().Equal(MonobankMerchant.Sandbox);
    }

    [Fact]
    public async Task HandleAsync_OrderWithoutMerchantRecorded_DefaultsToLive()
    {
        // Orders placed before the two-merchant change carry no merchant. Defaulting to live keeps
        // their refunds refundable; defaulting to sandbox would leave real money unreturned.
        var order = BuildOrder();
        order.MonobankMerchant = null;
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        _monobankClientFactory.RequestedMerchants.Should().Equal(MonobankMerchant.Live);
    }

    [Fact]
    public async Task HandleAsync_ShouldCapAmount_WhenCallerRequestsOverRefund()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // One of two units is already delivered, so only 250000 kopecks are
        // refundable — the caller-supplied 999999 must be capped down.
        var result = await _handler.HandleAsync(new RefundOrderCommand
        {
            OrderId = order.Id,
            AmountKopecks = 999999
        });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(250000);

        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var refund = await _context.Payments.SingleAsync(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund);
        refund.AmountKopecks.Should().Be(250000);
    }

    [Fact]
    public async Task HandleAsync_ShouldRespectCallerAmount_WhenBelowCap()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // A partial refund below the refundable cap is the caller's choice.
        var result = await _handler.HandleAsync(new RefundOrderCommand
        {
            OrderId = order.Id,
            AmountKopecks = 100000
        });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(100000);

        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 100000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotChangeOrderStatus_UntilMonobankConfirms()
    {
        var order = BuildOrder(OrderStatus.PendingFulfillment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PendingFulfillment);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnExistingRefund_WhenAlreadyExists()
    {
        var order = BuildOrder();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        SeedRefundPayment(order, PaymentStatus.Pending);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(500000);
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var refund = await _context.Payments.SingleAsync(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund);
        refund.AmountKopecks.Should().Be(500000);

        // An in-flight (Processing) refund must not move the order into a refunded state.
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyFulfilled);
    }

    [Fact]
    public async Task HandleAsync_ShouldReconcileOrderStatus_WhenExistingRefundCompleted()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        SeedRefundPayment(order, PaymentStatus.Succeeded);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Completed");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyRefunded);
    }

    /// <summary>
    /// The status branch and the amount branch apply the same rule by two different mechanisms, and
    /// only one of them looks like it needs an explicit filter. This pins the status branch.
    ///
    /// A delivery the reconciliation job released is not a delivery the customer received - that is
    /// what <c>FulfillmentConfiguration</c>'s global filter is <em>for</em>. So when every
    /// fulfillment on an order was released, nothing was delivered and the honest status is
    /// <c>Refunded</c>, not <c>PartiallyRefunded</c>.
    ///
    /// This was filed as a bug first (the count is missing an <c>IgnoreQueryFilters()</c>, which is
    /// what an audit notices) and it is not one - adding that call here reinstates the S9 bug pinned
    /// by <see cref="HandleAsync_ShouldNotCountReleasedFulfillmentsAsDelivered"/>. The test exists so
    /// the next audit does not "fix" it again.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ShouldReportFullyRefunded_WhenEveryFulfillmentWasReleased()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ReleasedAtUtc = DateTime.UtcNow,
            ReleaseReason = "stock exhausted",
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        SeedRefundPayment(order, PaymentStatus.Succeeded);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Completed");
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Refunded,
            "a released delivery is not a delivery, so nothing was delivered");
    }

    /// <summary>
    /// The boundary between the two statuses: one released and one live means fuel did leave the
    /// building for part of the order, which is exactly what PartiallyRefunded means.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ShouldReportPartiallyRefunded_WhenSomeFulfillmentsWereReleased()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 2,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ReleasedAtUtc = DateTime.UtcNow,
            ReleaseReason = "quantity_mismatch",
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        SeedRefundPayment(order, PaymentStatus.Succeeded);
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyRefunded,
            "one live delivery means part of the order did reach the customer");
    }

    [Fact]
    public async Task HandleAsync_ShouldCountFulfilledVouchers_WhenProviderCaseDiffers()
    {
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(250000);

        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNothingToRefund_WhenFullyFulfilled()
    {
        var order = BuildOrder(OrderStatus.Fulfilled);
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 2,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("NothingToRefund");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNotPayable_WhenNoMonobankInvoice()
    {
        var order = BuildOrder();
        order.MonobankInvoiceId = null;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("NotPayable");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNotFound_WhenOrderMissing()
    {
        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = Guid.NewGuid() });

        result.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task HandleAsync_ShouldMarkFailed_WhenMonobankThrows()
    {
        var order = BuildOrder();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Monobank API returned 502"));

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("502");

        var refund = await _context.Payments.SingleAsync(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund);
        refund.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task HandleAsync_ShouldRetryFailedRefund_InPlace()
    {
        // This suite's context runs NoTracking (see the fixture), which is what makes the assertion
        // below a real one rather than an accident of the production TrackAll default. A retry has to
        // bring the row back to life on the instance the change tracker knows about: reload the refund
        // untracked and every assignment is dropped, so the handler still calls Monobank and still
        // answers "Processing" while the row sits at Failed and the attempt leaves no record. That is
        // a money-path regression this file exists to catch.
        var order = BuildOrder();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        SeedRefundPayment(order, PaymentStatus.Failed);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 500000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var refunds = await _context.Payments.Where(r => r.OrderId == order.Id && r.Kind == PaymentKind.Refund).ToListAsync();
        refunds.Should().ContainSingle();
        refunds[0].Status.Should().Be(PaymentStatus.Pending);
        refunds[0].ErrorMessage.Should().BeNull();
        // The brand's word is not stored on the refund any more; Pending is the record.
        refunds[0].AmountKopecks.Should().Be(500000);
    }

    [Fact]
    public void ComputeFulfilledValue_ShouldBeZero_WhenOrderWasNeverPaid()
    {
        // Guards the accounting, not the refund. Fulfilled value is derived as
        // `total - refundable`, so once an unpayable order reports nothing refundable the naive
        // subtraction returns the FULL order price - and GetReport/GetReconciliation would book
        // an abandoned checkout as delivered revenue. Nothing was delivered, so it is zero.
        var order = BuildOrder(OrderStatus.PendingPayment, MonobankStatus.Pending);

        RefundOrderCommandHandler.ComputeRefundAmountKopecks(order).Should().Be(0);
        RefundOrderCommandHandler.ComputeFulfilledValueKopecks(order).Should().Be(0);
    }

    [Fact]
    public void ComputeFulfilledValue_ShouldStillCountValue_WhenCancelledOrderWasPaid()
    {
        // The counterpart. Money that arrived and goods not delivered is still revenue that
        // must appear, and must remain refundable.
        var order = BuildOrder(OrderStatus.Cancelled);

        // Nothing was delivered (no fulfilments exist on this fixture), so fulfilled value is zero
        // while the whole amount stays refundable. The point is that neither figure is derived
        // from OrderStatus here - both come from the brand's payment truth.
        RefundOrderCommandHandler.ComputeFulfilledValueKopecks(order).Should().Be(0);
        RefundOrderCommandHandler.ComputeRefundAmountKopecks(order).Should().Be(500000);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuse_WhenOrderWasNeverPaid()
    {
        // An unpaid checkout has an invoice and a full line item, so before this gate it reported
        // its entire price as refundable and the admin button rendered as a live action.
        var order = BuildOrder(OrderStatus.PendingPayment, MonobankStatus.Pending);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("NothingToRefund");
        result.AmountKopecks.Should().Be(0);
        // Names the actual reason: the old message claimed all value was delivered, which is
        // untrue and read to operators as a completed order.
        result.ErrorMessage.Should().Contain("not paid");

        // Never reached out to Monobank for money that was never collected.
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await _context.Payments.CountAsync(p => p.Kind == PaymentKind.Refund)).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldStillRefund_WhenCancelledOrderWasPaidAfterCancellation()
    {
        // The production incident this gate must not break.
        //
        // A `failure` cancelled the order, but the Monobank invoice stays payable for its whole
        // validity window - so the customer paid it later from a stale browser tab. The `success`
        // landed on a terminal state and was refused, which is correct: goods must not be issued
        // for a cancelled order. A manual refund is then the only way to return the money.
        var order = BuildOrder(OrderStatus.Cancelled);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 500000, "idem-key-1", It.IsAny<IReadOnlyList<MonobankBasketItem>>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Cancelled is NOT itself a reason to refuse. Only the brand's word about payment
        // decides, so this stays refundable.
        RefundOrderCommandHandler.ComputeRefundAmountKopecks(order).Should().Be(500000);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuse_WhenInvoiceCreationFailedAndNoInvoiceExists()
    {
        // Checkout failed before it reached the brand: no invoice, nothing charged.
        var order = BuildOrder(OrderStatus.PendingPayment, MonobankStatus.Pending);
        order.MonobankInvoiceId = null;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        // Distinct from "nothing left to return": here the reason is structural - there was
        // never an invoice to reverse, because checkout failed before it reached the brand.
        result.Status.Should().Be("NotPayable");
        result.ErrorMessage.Should().Contain("no Monobank invoice");
        (await _context.Payments.CountAsync(p => p.Kind == PaymentKind.Refund)).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldNotCountReleasedFulfillmentsAsDelivered()
    {
        // Two units ordered, one delivered, and the second delivery was released by the
        // reconciliation job because stock ran out. Half the value is still owed to the customer.
        //
        // The handler loads the order with IgnoreQueryFilters() so it can see soft-deleted orders,
        // and that also switches off Fulfillment's global `ReleasedAtUtc == null` filter. Counting
        // released rows as delivered made fulfilledUnits (2) meet OrderedUnits (2), the unfulfilled
        // value collapse to zero, and the customer was told "no unfulfilled value remains" while
        // half their money stayed with us. The admin dialog showed it as "0.00 ╨│╤Ç╨╜".
        var order = BuildOrder();
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 1,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow,
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        order.Fulfillments.Add(new Fulfillment
        {
            Id = 2,
            OrderId = order.Id,
            VoucherId = Guid.NewGuid(),
            FulfilledAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ReleasedAtUtc = DateTime.UtcNow,
            ReleaseReason = "stock exhausted",
            Voucher = BuildVoucher("okko", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing", "one of two delivered units is still owed back");
        result.AmountKopecks.Should().Be(250000, "a released delivery is not a delivery");
    }
}
