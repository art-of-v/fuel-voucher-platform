using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Orders.SharedModels;
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

    private static Refund BuildRefund(Guid orderId, RefundStatus status = RefundStatus.Processing) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        UserId = Guid.NewGuid(),
        Amount = 52000,
        InvoiceId = "INV-REFUND-1",
        ExtRef = "idem-key-1",
        Status = status,
        IsAutomatic = false,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
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
