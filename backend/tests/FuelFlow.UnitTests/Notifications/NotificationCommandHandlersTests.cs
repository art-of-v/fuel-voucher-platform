using FluentAssertions;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Notifications;

public sealed class NotificationCommandHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public NotificationCommandHandlersTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static Notification CreateNotification(Guid id, Guid userId, string title, bool isRead, DateTime createdAtUtc)
    {
        return new Notification
        {
            Id = id,
            UserId = userId,
            Title = title,
            Message = $"Message for {title}",
            IsRead = isRead,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    [Fact]
    public async Task GetNotifications_ShouldReturnNotificationsForUserOnly()
    {
        var first = CreateNotification(Guid.NewGuid(), UserId, "First", false, DateTime.UtcNow.AddHours(-2));
        var second = CreateNotification(Guid.NewGuid(), UserId, "Second", true, DateTime.UtcNow.AddHours(-1));
        var other = CreateNotification(Guid.NewGuid(), OtherUserId, "Other", false, DateTime.UtcNow);
        _context.Notifications.AddRange(first, second, other);
        await _context.SaveChangesAsync();

        var handler = new GetNotificationsQueryHandler(_context);
        var query = new GetNotificationsQuery(UserId);

        var notifications = await handler.HandleAsync(query);

        notifications.Should().HaveCount(2);
        notifications.Should().Contain(n => n.Id == first.Id);
        notifications.Should().Contain(n => n.Id == second.Id);
        notifications.Should().NotContain(n => n.Id == other.Id);
        notifications[0].CreatedAt.Should().BeAfter(notifications[1].CreatedAt);
    }

    [Fact]
    public async Task GetNotifications_ShouldReturnEmpty_WhenNone()
    {
        var handler = new GetNotificationsQueryHandler(_context);
        var query = new GetNotificationsQuery(UserId);

        var notifications = await handler.HandleAsync(query);

        notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkNotificationRead_ShouldMarkRead()
    {
        var notification = CreateNotification(Guid.NewGuid(), UserId, "Title", false, DateTime.UtcNow);
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var handler = new MarkNotificationReadCommandHandler(_context);
        var command = new MarkNotificationReadCommand(notification.Id, UserId);

        var response = await handler.HandleAsync(command);

        response.Should().NotBeNull();
        response!.Id.Should().Be(notification.Id);
        response.IsRead.Should().BeTrue();

        var updated = await _context.Notifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task MarkNotificationRead_ShouldReturnNull_WhenNotificationMissing()
    {
        var handler = new MarkNotificationReadCommandHandler(_context);
        var command = new MarkNotificationReadCommand(Guid.NewGuid(), UserId);

        var response = await handler.HandleAsync(command);

        response.Should().BeNull();
    }

    [Fact]
    public async Task MarkNotificationRead_ShouldReturnNull_WhenNotificationBelongsToAnotherUser()
    {
        var notification = CreateNotification(Guid.NewGuid(), OtherUserId, "Title", false, DateTime.UtcNow);
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var handler = new MarkNotificationReadCommandHandler(_context);
        var command = new MarkNotificationReadCommand(notification.Id, UserId);

        var response = await handler.HandleAsync(command);

        response.Should().BeNull();

        var updated = await _context.Notifications.FindAsync(notification.Id);
        updated!.IsRead.Should().BeFalse();
    }
}
