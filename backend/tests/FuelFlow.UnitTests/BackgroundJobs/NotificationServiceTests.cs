using FluentAssertions;
using FuelFlow.API.BackgroundJobs.Models;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.BackgroundJobs;

public sealed class ApiNotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IExpoPushSender> _pushSender = new();
    private readonly FuelFlow.API.BackgroundJobs.NotificationService _service;

    public ApiNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        // Default: the sender accepts everything. Individual tests override to exercise
        // DeviceNotRegistered handling or a thrown failure.
        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExpoPushResult>());

        _service = new FuelFlow.API.BackgroundJobs.NotificationService(
            _context,
            _pushSender.Object,
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

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldPushToEveryActiveTokenOfUser()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await SeedPushTokenAsync(userId, "ExponentPushToken[active-1]", isActive: true);
        await SeedPushTokenAsync(userId, "ExponentPushToken[active-2]", isActive: true);
        await SeedPushTokenAsync(userId, "ExponentPushToken[inactive]", isActive: false);
        await SeedPushTokenAsync(Guid.NewGuid(), "ExponentPushToken[other-user]", isActive: true);

        IReadOnlyList<ExpoPushMessage>? sent = null;
        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ExpoPushMessage>, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(new List<ExpoPushResult>());

        _context.OutboxEvents.Add(CreateOrderFulfilledEvent(orderId, userId, processed: false));
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        sent.Should().NotBeNull();
        sent!.Select(m => m.Token).Should().BeEquivalentTo(
            "ExponentPushToken[active-1]", "ExponentPushToken[active-2]");
        sent.Should().OnlyContain(m =>
            m.Title == "Замовлення виконано" && m.Body.Contains(orderId.ToString()));
    }
    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldDeactivateTokenReportedDeviceNotRegistered()
    {
        var userId = Guid.NewGuid();
        const string dead = "ExponentPushToken[dead]";
        await SeedPushTokenAsync(userId, dead, isActive: true);

        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExpoPushResult> { new(dead, ExpoPushStatus.Error, "DeviceNotRegistered") });

        _context.OutboxEvents.Add(CreateOrderFulfilledEvent(Guid.NewGuid(), userId, processed: false));
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        var token = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Token == dead);
        token.IsActive.Should().BeFalse();
    }
    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldStillCreateNotification_WhenPushSenderThrows()
    {
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await SeedPushTokenAsync(userId, "ExponentPushToken[boom]", isActive: true);

        _pushSender
            .Setup(x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Expo unreachable"));

        var outboxEvent = CreateOrderFulfilledEvent(orderId, userId, processed: false);
        _context.OutboxEvents.Add(outboxEvent);
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        // Push is best-effort: a sender failure must not block the in-app notification
        // nor leave the event unprocessed (which would reprocess and duplicate it).
        (await _context.Notifications.CountAsync(n => n.UserId == userId)).Should().Be(1);
        var processedEvent = await _context.OutboxEvents.FindAsync(outboxEvent.Id);
        processedEvent!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessOrderFulfilledEventsAsync_ShouldNotCallSender_WhenUserHasNoActiveTokens()
    {
        var userId = Guid.NewGuid();
        await SeedPushTokenAsync(userId, "ExponentPushToken[inactive-only]", isActive: false);

        _context.OutboxEvents.Add(CreateOrderFulfilledEvent(Guid.NewGuid(), userId, processed: false));
        await _context.SaveChangesAsync();

        await _service.ProcessOrderFulfilledEventsAsync();

        _pushSender.Verify(
            x => x.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    private async Task SeedPushTokenAsync(Guid userId, string token, bool isActive)
    {
        var now = DateTime.UtcNow;
        _context.PushTokens.Add(new UserPushToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = "ios",
            DeviceId = null,
            IsActive = isActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastSeenAtUtc = now
        });
        await _context.SaveChangesAsync();
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
