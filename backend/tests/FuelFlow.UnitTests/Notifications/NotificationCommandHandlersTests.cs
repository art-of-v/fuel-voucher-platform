using FluentAssertions;
using FuelFlow.Features.Notifications.GetNotifications;
using FuelFlow.Features.Notifications.MarkNotificationRead;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
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

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
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
        var first = CreateNotification(Guid.NewGuid(), UserId, "First", false, TestClock.UtcNow.AddHours(-2));
        var second = CreateNotification(Guid.NewGuid(), UserId, "Second", true, TestClock.UtcNow.AddHours(-1));
        var other = CreateNotification(Guid.NewGuid(), OtherUserId, "Other", false, TestClock.UtcNow);
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

    /// <summary>
    /// The type and the target are the contract: the copy is prose, and a client that has to
    /// understand the prose to route a notification is a client that breaks when the prose changes.
    /// </summary>
    [Fact]
    public async Task GetNotifications_ShouldCarryTheTypeAndTheTarget()
    {
        var companyId = Guid.NewGuid();
        var notification = CreateNotification(Guid.NewGuid(), UserId, "Вам видано паливо", false, TestClock.UtcNow);
        notification.Type = NotificationTypes.VoucherIssued;
        notification.Payload = NotificationPayload.Serialize(new NotificationPayload
        {
            LegalEntityId = companyId.ToString(),
            VoucherCount = 3,
        });
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var handler = new GetNotificationsQueryHandler(_context);

        var dto = (await handler.HandleAsync(new GetNotificationsQuery(UserId))).Single();

        dto.Type.Should().Be(NotificationTypes.VoucherIssued);
        dto.LegalEntityId.Should().Be(companyId);
        dto.OrderId.Should().BeNull();
    }

    /// <summary>
    /// Every row written before the type existed reads as what it is: a plain row with nothing to
    /// open. The columns are added with a default, so the old rows are not a migration problem -
    /// but they are a reading problem if the reader expects a target that is not there.
    /// </summary>
    [Fact]
    public async Task GetNotifications_ShouldReadAnOldRowAsAPlainRow()
    {
        var notification = CreateNotification(Guid.NewGuid(), UserId, "Замовлення виконано", true, TestClock.UtcNow);
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var handler = new GetNotificationsQueryHandler(_context);

        var dto = (await handler.HandleAsync(new GetNotificationsQuery(UserId))).Single();

        dto.Type.Should().Be(NotificationTypes.Generic);
        dto.LegalEntityId.Should().BeNull();
        dto.OrderId.Should().BeNull();
    }

    /// <summary>
    /// A payload that is not JSON is a broken row, not a broken request. The list still reads.
    /// </summary>
    [Fact]
    public async Task GetNotifications_ShouldSurviveABrokenPayload()
    {
        var notification = CreateNotification(Guid.NewGuid(), UserId, "Вам видано паливо", false, TestClock.UtcNow);
        notification.Type = NotificationTypes.VoucherIssued;
        notification.Payload = "{not json";
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var handler = new GetNotificationsQueryHandler(_context);

        var dto = (await handler.HandleAsync(new GetNotificationsQuery(UserId))).Single();

        dto.Type.Should().Be(NotificationTypes.VoucherIssued);
        dto.LegalEntityId.Should().BeNull();
        dto.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task MarkNotificationRead_ShouldMarkRead()
    {
        var notification = CreateNotification(Guid.NewGuid(), UserId, "Title", false, TestClock.UtcNow);
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
        var notification = CreateNotification(Guid.NewGuid(), OtherUserId, "Title", false, TestClock.UtcNow);
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
