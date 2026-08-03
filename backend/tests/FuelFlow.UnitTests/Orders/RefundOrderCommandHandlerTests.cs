using FluentAssertions;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Vouchers;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FuelFlow.UnitTests.Orders;

public sealed class RefundOrderCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock;
    private readonly RefundOrderCommandHandler _handler;

    public RefundOrderCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        _monobankClientMock = new Mock<IMonobankClient>();
        _monobankClientMock
            .Setup(x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankCancelResponse { Status = "processing" });

        _handler = new RefundOrderCommandHandler(
            _context,
            _monobankClientMock.Object,
            new ProviderEventService(_context));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static Order BuildOrder(OrderStatus status = OrderStatus.PartiallyFulfilled) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Price = 5000,
        Status = status,
        MonobankInvoiceId = "INV123",
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
        ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
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

        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.PartiallyRefunded);
    }

    [Fact]
    public async Task HandleAsync_ShouldMarkOrderRefunded_WhenNothingDelivered()
    {
        var order = BuildOrder();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var result = await _handler.HandleAsync(new RefundOrderCommand { OrderId = order.Id });

        result.Status.Should().Be("Processing");
        var updatedOrder = await _context.Orders.FindAsync(order.Id);
        updatedOrder!.Status.Should().Be(OrderStatus.Refunded);
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
        _monobankClientMock.Verify(
            x => x.CancelInvoiceAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
}
