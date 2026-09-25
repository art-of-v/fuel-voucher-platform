using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Orders.SharedServices.Monobank;
using FuelFlow.API.Features.Orders.SharedServices.Monobank.Models;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Observability;
using FuelFlow.SharedKernel.Options;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

/// <summary>
/// The reconciliation poller feeds polled invoice statuses through the real
/// <see cref="ProcessMonobankWebhookCommandHandler"/>, so these tests construct a genuine handler
/// (not a mock) and assert on the persisted order, the ORDER_CREATED outbox row and the fulfillment
/// enqueue - proving a poll-driven recovery is identical to a webhook-driven one.
/// </summary>
public sealed class MonobankReconciliationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IMonobankClient> _monobankClientMock = new();
    private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock = new();
    private readonly ProcessMonobankWebhookCommandHandler _handler;

    public MonobankReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            // Mirror production: the API context defaults to NoTracking, so the handler's
            // load+Update path must survive it. The reconciliation query projects (never tracks).
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _context = new ApplicationDbContext(options);

        _handler = new ProcessMonobankWebhookCommandHandler(
            _context,
            new Mock<ILogger<ProcessMonobankWebhookCommandHandler>>().Object,
            _backgroundJobClientMock.Object,
            new Mock<RefundStatusSyncService>(null!, null!, null!).Object,
            new FuelFlowMetrics(),
            NotificationDispatcher.Disabled);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private MonobankReconciliationService CreateService(
        bool enabled = true, bool reconciliationEnabled = true, int batchSize = 100) =>
        new(
            _context,
            _monobankClientMock.Object,
            _handler,
            new FuelFlowMetrics(),
            Options.Create(new MonobankOptions
            {
                Enabled = enabled,
                ReconciliationEnabled = reconciliationEnabled,
                ReconciliationMinAgeMinutes = 3,
                ReconciliationMaxAgeHours = 24,
                ReconciliationBatchSize = batchSize
            }),
            new Mock<ILogger<MonobankReconciliationService>>().Object);

    // Old enough to have passed the 3-minute grace window, young enough to be inside the 24h window.
    private static DateTime Aged() => DateTime.UtcNow.AddMinutes(-10);

    private void SetupInvoice(string invoiceId, string status, long amount = 250000) =>
        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync(invoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonobankInvoiceStatus
            {
                InvoiceId = invoiceId,
                Status = status,
                Amount = amount,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            });

    private async Task<Order> AddOrderAsync(string invoiceId, OrderStatus status, DateTime createdAtUtc)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Price = 2500, // Money.ToKopecks(2500) == 250000, matching the default SetupInvoice amount
            Status = status,
            MonobankInvoiceId = invoiceId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            LineItems =
            {
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    Provider = "okko",
                    FuelTypeId = "okko-95",
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        // Detach the seeded graph so the poller and the handler read the order through fresh,
        // untracked queries - exactly as their request-scoped context would in production. Without
        // this, the seed instance stays tracked and the handler's Update() hits a duplicate-key
        // tracking conflict that never occurs against a real, per-request DbContext.
        _context.ChangeTracker.Clear();
        return order;
    }

    private void VerifyEnqueued(Times times) =>
        _backgroundJobClientMock.Verify(x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()), times);

    [Fact]
    public async Task Reconcile_WhenInvoicePaid_RecoversOrderAndEnqueuesFulfillment()
    {
        var order = await AddOrderAsync("INV-PAID", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-PAID", "success");

        await CreateService().ReconcilePendingPaymentsAsync();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        (await _context.OutboxEvents.SingleOrDefaultAsync(e => e.EventType == OutboxEventType.OrderCreated))
            .Should().NotBeNull();
        VerifyEnqueued(Times.Once());
    }

    [Fact]
    public async Task Reconcile_WhenInvoiceExpired_CancelsOrder()
    {
        var order = await AddOrderAsync("INV-EXP", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-EXP", "expired");

        await CreateService().ReconcilePendingPaymentsAsync();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
        _context.OutboxEvents.Should().BeEmpty();
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenInvoiceFailed_CancelsOrder()
    {
        var order = await AddOrderAsync("INV-FAIL", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-FAIL", "failure");

        await CreateService().ReconcilePendingPaymentsAsync();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenInvoiceStillProcessing_LeavesOrderAwaitingPayment()
    {
        var order = await AddOrderAsync("INV-PROC", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-PROC", "processing");

        await CreateService().ReconcilePendingPaymentsAsync();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
        _context.OutboxEvents.Should().BeEmpty();
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenOrderInsideGraceWindow_IsNotPolled()
    {
        // Younger than ReconciliationMinAgeMinutes: the webhook is still given time to arrive first.
        await AddOrderAsync("INV-NEW", OrderStatus.PendingPayment, DateTime.UtcNow);
        SetupInvoice("INV-NEW", "success");

        await CreateService().ReconcilePendingPaymentsAsync();

        _monobankClientMock.Verify(
            x => x.GetInvoiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenOrderOlderThanMaxAge_IsNotPolled()
    {
        // Past the 24h window: the Monobank invoice has long since expired, so stop chasing it.
        await AddOrderAsync("INV-OLD", OrderStatus.PendingPayment, DateTime.UtcNow.AddHours(-25));
        SetupInvoice("INV-OLD", "success");

        await CreateService().ReconcilePendingPaymentsAsync();

        _monobankClientMock.Verify(
            x => x.GetInvoiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenMonobankDisabled_DoesNotPoll()
    {
        // Monobank disabled => the in-process mock is bound; polling it would fabricate a "success".
        await AddOrderAsync("INV-OFF", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-OFF", "success");

        await CreateService(enabled: false).ReconcilePendingPaymentsAsync();

        _monobankClientMock.Verify(
            x => x.GetInvoiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenReconciliationDisabled_DoesNotPoll()
    {
        await AddOrderAsync("INV-RECOFF", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-RECOFF", "success");

        await CreateService(reconciliationEnabled: false).ReconcilePendingPaymentsAsync();

        _monobankClientMock.Verify(
            x => x.GetInvoiceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenPaidAmountMismatch_DoesNotRecover()
    {
        // The shared handler's amount guard must still fire on the poll path: a "success" whose
        // charged amount is not the server-side order price is rejected, order left untouched.
        var order = await AddOrderAsync("INV-AMT", OrderStatus.PendingPayment, Aged());
        SetupInvoice("INV-AMT", "success", amount: 1);

        await CreateService().ReconcilePendingPaymentsAsync();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
        _context.OutboxEvents.Should().BeEmpty();
        VerifyEnqueued(Times.Never());
    }

    [Fact]
    public async Task Reconcile_WhenOneInvoiceThrows_ContinuesWithTheRest()
    {
        // A network blip or a 404 on one purged invoice must not abort the whole batch.
        var failing = await AddOrderAsync("INV-ERR", OrderStatus.PendingPayment, DateTime.UtcNow.AddMinutes(-15));
        var healthy = await AddOrderAsync("INV-OK2", OrderStatus.PendingPayment, DateTime.UtcNow.AddMinutes(-10));

        _monobankClientMock
            .Setup(x => x.GetInvoiceStatusAsync("INV-ERR", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        SetupInvoice("INV-OK2", "success");

        await CreateService().ReconcilePendingPaymentsAsync();

        (await _context.Orders.FindAsync(failing.Id))!.Status.Should().Be(OrderStatus.PendingPayment);
        (await _context.Orders.FindAsync(healthy.Id))!.Status.Should().Be(OrderStatus.PendingFulfillment);
        VerifyEnqueued(Times.Once());
    }
}
