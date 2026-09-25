using System.Linq.Expressions;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Persistence;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.Monobank;

public sealed class ProcessMonobankWebhookCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ProcessMonobankWebhookCommandHandler _handler;
    private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;

    public ProcessMonobankWebhookCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _backgroundJobClientMock = new Mock<IBackgroundJobClient>();

        _handler = new ProcessMonobankWebhookCommandHandler(
            _context,
            new Mock<ILogger<ProcessMonobankWebhookCommandHandler>>().Object,
            _backgroundJobClientMock.Object,
            new Mock<RefundStatusSyncService>(null!, null!, null!).Object,
            new FuelFlow.SharedKernel.Observability.FuelFlowMetrics(),
            FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static Order BuildOrder(Guid userId, string invoiceId, OrderStatus status) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Price = 2500,
        Status = status,
        MonobankInvoiceId = invoiceId,
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
                Quantity = 1,
                UnitPrice = 2500,
                LineTotal = 2500
            }
        }
    };

    private static ProcessMonobankWebhookCommand WebhookCommand(string invoiceId, string status) => new()
    {
        InvoiceId = invoiceId,
        Status = status,
        Amount = 250000,
        CreatedDate = DateTime.UtcNow,
        ModifiedDate = DateTime.UtcNow
    };

    [Fact]
    public async Task ProcessWebhook_Success_ShouldSetPendingFulfillment()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-001", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-001", "success"));

        response.Success.Should().BeTrue();
        response.OrderId.Should().Be(order.Id.ToString());
        response.PreviousStatus.Should().Be(OrderStatus.PendingPayment.ToString());
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        var outboxEvent = await _context.OutboxEvents.SingleOrDefaultAsync(e => e.EventType == OutboxEventType.OrderCreated);
        outboxEvent.Should().NotBeNull();
        outboxEvent!.Processed.Should().BeFalse();
        outboxEvent.Payload.Should().Contain(order.Id.ToString());

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhook_Failure_ShouldCancelOrder()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-002", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-002", "failure"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.Cancelled.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_ShouldReturnSuccessFalse_WhenOrderNotFound()
    {
        var response = await _handler.HandleAsync(WebhookCommand("INV-MISSING", "success"));

        response.Success.Should().BeFalse();
        response.OrderId.Should().BeNull();
        response.Message.Should().Contain("not found");
        response.ErrorCode.Should().Be("NOT_FOUND");

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_OnSoftDeletedOrder_ShouldRestoreAndFulfill()
    {
        // #23: a customer can swipe-delete an unpaid checkout (soft delete) while its Monobank
        // invoice stays live. A later "success" must still find the order, restore it (un-hide),
        // and fulfill — never leave the customer charged with no voucher.
        var order = BuildOrder(Guid.NewGuid(), "INV-DEL", OrderStatus.PendingPayment);
        order.IsDeleted = true;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-DEL", "success"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        // FindAsync bypasses the global !IsDeleted query filter, so we can read the restored row back.
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.IsDeleted.Should().BeFalse();
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhook_Success_WithAmountMismatch_ShouldNotTransition()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-AMT", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-AMT", "success");
        command.Amount = 1; // does not match order.Price * 100

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("AMOUNT_MISMATCH");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
        updated.LastWebhookModifiedDateUtc.Should().BeNull();

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_StaleModifiedDate_ShouldAcknowledgeWithoutTransition()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-STALE", OrderStatus.PendingFulfillment);
        order.LastWebhookModifiedDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-STALE", "success");
        command.ModifiedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());
        response.Message.Should().Contain("Stale");

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_DuplicateSuccess_WhenAlreadyPendingFulfillment_ShouldNotReEnqueue()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-DUP", OrderStatus.PendingFulfillment);
        order.LastWebhookModifiedDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-DUP", "success");
        command.ModifiedDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());
        response.Message.Should().Contain("Duplicate");

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_OnFulfilledOrder_ShouldBeRejectedAsIllegalTransition()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-FUL", OrderStatus.Fulfilled);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-FUL", "success"));

        response.Success.Should().BeTrue();
        response.Message.Should().Contain("Illegal transition");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Fulfilled);

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Reversed_ShouldCancelOrder()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-REV", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-REV", "reversed"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.Cancelled.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessWebhook_Success_ShouldRecordLastWebhookTracking()
    {
        var order = BuildOrder(Guid.NewGuid(), "INV-TRACK", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-TRACK", "success"));

        response.Success.Should().BeTrue();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.LastWebhookProcessedAtUtc.Should().NotBeNull();
        updated.LastWebhookModifiedDateUtc.Should().NotBeNull();
    }
}
