using FluentAssertions;
using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

public sealed class ApiNotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlow.API.BackgroundJobs.NotificationService _service;

    public ApiNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new FuelFlow.API.BackgroundJobs.NotificationService(
            _context,
            new Mock<ILogger<FuelFlow.API.BackgroundJobs.NotificationService>>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static OutboxEvent CreateOrderFulfilledEvent(Guid orderId, Guid userId, bool processed)
    {
        return new OutboxEvent
        {
            EventType = OutboxEventType.OrderFulfilled,
            // Mirror the production producer (FulfillmentService): a camelCase anonymous
            // object. Serialising a typed OrderFulfilledPayload here would emit PascalCase
            // and hide the case-sensitivity bug this test now guards against.
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId,
                userId,
                fulfilledAt = DateTime.UtcNow
            }),
            Processed = processed,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldCreateNotificationAndMarkEventProcessed()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var outboxEvent = CreateOrderFulfilledEvent(orderId, userId, processed: false);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        var notification = await _context.Notifications.SingleAsync(n => n.UserId == userId);
        notification.Title.Should().Be("Замовлення виконано");
        notification.Message.Should().Contain(orderId.ToString());
        notification.IsRead.Should().BeFalse();
        notification.CreatedAtUtc.Should().NotBe(default);

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldSkipAlreadyProcessedEvent()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var outboxEvent = CreateOrderFulfilledEvent(orderId, userId, processed: true);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId)
            .ToListAsync();
        notifications.Should().BeEmpty();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldMarkInvalidPayloadEventAsProcessed_WithoutCreatingNotification()
    {
        var outboxEvent = new OutboxEvent
        {
            EventType = OutboxEventType.OrderFulfilled,
            Payload = System.Text.Json.JsonSerializer.Serialize(new OrderFulfilledPayload
            {
                OrderId = Guid.NewGuid(),
                UserId = null!,
                FulfilledAt = DateTime.UtcNow
            }),
            Processed = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        (await _context.Notifications.CountAsync()).Should().Be(0);

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();
    }
}

public sealed class JobsWorkerNotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly FuelFlow.JobsWorker.Services.NotificationService _service;

    public JobsWorkerNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new FuelFlow.JobsWorker.Services.NotificationService(
            _context,
            new Mock<ILogger<FuelFlow.JobsWorker.Services.NotificationService>>().Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static OutboxEvent CreateOrderFulfilledEvent(Guid orderId, Guid userId, bool processed)
    {
        return new OutboxEvent
        {
            EventType = OutboxEventType.OrderFulfilled,
            // Mirror the production producer (FulfillmentService): a camelCase anonymous
            // object. Serialising a typed OrderFulfilledPayload here would emit PascalCase
            // and hide the case-sensitivity bug this test now guards against.
            Payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                orderId,
                userId,
                fulfilledAt = DateTime.UtcNow
            }),
            Processed = processed,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldCreateNotificationAndMarkEventProcessed()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var outboxEvent = CreateOrderFulfilledEvent(orderId, userId, processed: false);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        var notification = await _context.Notifications.SingleAsync(n => n.UserId == userId);
        notification.Title.Should().Be("Замовлення виконано");
        notification.Message.Should().Contain(orderId.ToString());
        notification.IsRead.Should().BeFalse();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
        processedEvent.ProcessedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldSkipAlreadyProcessedEvent()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var outboxEvent = CreateOrderFulfilledEvent(orderId, userId, processed: true);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId)
            .ToListAsync();
        notifications.Should().BeEmpty();

        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent.Should().NotBeNull();
        processedEvent!.Processed.Should().BeTrue();
    }
}
