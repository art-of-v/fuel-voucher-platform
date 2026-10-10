using FluentAssertions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using Microsoft.EntityFrameworkCore;
using Moq;

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
            // Mirror production: the API registers the context with a NoTracking
            // default, so write paths must opt in via AsTracking. Tests must catch
            // mutations lost to detached entities.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        _monobankClientFactory = new StubMonobankClientFactory(_monobankClientMock.Object);
        _handler = new RefundOrderCommandHandler(
            _context,
            _monobankClientFactory,
            new ProviderEventService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
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
                Provider = "okko",
                FuelTypeId = "okko-95",
                Liters = 50,
                Quantity = 2,
                UnitPrice = 2500,
                LineTotal = 5000
            }
        }
    };

    private static FuelVoucher BuildVoucher(string provider, string fuelTypeId, decimal liters) => new()
    {
        Id = Guid.NewGuid(),
        Provider = provider,
        FuelTypeId = fuelTypeId,
        Liters = liters,
        VoucherNumber = $"V-{Guid.NewGuid():N}",
        QrPayload = "q",
        ProviderExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
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
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<CancellationToken>()),
            Times.Once);

        var refund = await _context.Refunds.SingleAsync(r => r.OrderId == order.Id);
        refund.Amount.Should().Be(250000);
        refund.InvoiceId.Should().Be("INV123");
        refund.IsAutomatic.Should().BeFalse();
        refund.Status.Should().Be(RefundStatus.Processing);

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
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<CancellationToken>()),
            Times.Once);

        var refund = await _context.Refunds.SingleAsync(r => r.OrderId == order.Id);
        refund.Amount.Should().Be(250000);
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
            x => x.CancelInvoiceAsync("INV123", 100000, "idem-key-1", It.IsAny<CancellationToken>()),
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

        _context.Refunds.Add(new Refund
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = 2500,
            InvoiceId = "INV123",
            ExtRef = "idem-key-1",
            Status = RefundStatus.Processing,
            IsAutomatic = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(500000);
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var refund = await _context.Refunds.SingleAsync(r => r.OrderId == order.Id);
        refund.Amount.Should().Be(500000);

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

        _context.Refunds.Add(new Refund
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = 250000,
            InvoiceId = "INV123",
            ExtRef = "idem-key-1",
            Status = RefundStatus.Completed,
            IsAutomatic = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Completed");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyRefunded);
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
            Voucher = BuildVoucher("OKKO", "okko-95", 50)
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        result.AmountKopecks.Should().Be(250000);

        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 250000, "idem-key-1", It.IsAny<CancellationToken>()),
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
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
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
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Monobank API returned 502"));

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("502");

        var refund = await _context.Refunds.SingleAsync(r => r.OrderId == order.Id);
        refund.Status.Should().Be(RefundStatus.Failed);
    }

    [Fact]
    public async Task HandleAsync_ShouldRetryFailedRefund_InPlace()
    {
        var order = BuildOrder();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _context.Refunds.Add(new Refund
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = 2500,
            InvoiceId = "INV123",
            ExtRef = "idem-key-1",
            Status = RefundStatus.Failed,
            ErrorMessage = "previous failure",
            IsAutomatic = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync("INV123", 500000, "idem-key-1", It.IsAny<CancellationToken>()),
            Times.Once);

        var refunds = await _context.Refunds.Where(r => r.OrderId == order.Id).ToListAsync();
        refunds.Should().ContainSingle();
        refunds[0].Status.Should().Be(RefundStatus.Processing);
        refunds[0].ErrorMessage.Should().BeNull();
        refunds[0].MonobankStatus.Should().Be("processing");
        refunds[0].Amount.Should().Be(500000);
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
        // from OrderStatus here - both come from the provider's payment truth.
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
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await _context.Refunds.CountAsync()).Should().Be(0);
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
            x => x.CancelInvoiceAsync("INV123", 500000, "idem-key-1", It.IsAny<CancellationToken>()),
            Times.Once);

        // Cancelled is NOT itself a reason to refuse. Only the provider's word about payment
        // decides, so this stays refundable.
        RefundOrderCommandHandler.ComputeRefundAmountKopecks(order).Should().Be(500000);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuse_WhenInvoiceCreationFailedAndNoInvoiceExists()
    {
        // Checkout failed before it reached the provider: no invoice, nothing charged.
        var order = BuildOrder(OrderStatus.PendingPayment, MonobankStatus.Pending);
        order.MonobankInvoiceId = null;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        // Distinct from "nothing left to return": here the reason is structural - there was
        // never an invoice to reverse, because checkout failed before it reached the provider.
        result.Status.Should().Be("NotPayable");
        result.ErrorMessage.Should().Contain("no Monobank invoice");
        (await _context.Refunds.CountAsync()).Should().Be(0);
    }
}
