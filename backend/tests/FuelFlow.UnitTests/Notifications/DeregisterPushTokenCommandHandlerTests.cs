using FluentAssertions;
using FuelFlow.Features.Notifications.DeregisterPushToken;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Notifications;

public sealed class DeregisterPushTokenCommandHandlerTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;
    private readonly DeregisterPushTokenCommandHandler _handler;

    public DeregisterPushTokenCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _handler = new DeregisterPushTokenCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<UserPushToken> SeedAsync(Guid userId, string token, string? deviceId, bool isActive)
    {
        var now = DateTime.UtcNow;
        var entity = new UserPushToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = "ios",
            DeviceId = deviceId,
            IsActive = isActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastSeenAtUtc = now
        };
        _context.PushTokens.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }
    [Fact]
    public async Task HandleAsync_ShouldNoOp_WhenNeitherTokenNorDeviceIdProvided()
    {
        var seeded = await SeedAsync(UserId, "ExponentPushToken[a]", "device-1", isActive: true);

        var response = await _handler.HandleAsync(
            new DeregisterPushTokenCommand(UserId, Token: null, DeviceId: null));

        response.Deactivated.Should().Be(0);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldDeactivateByDeviceId()
    {
        var seeded = await SeedAsync(UserId, "ExponentPushToken[a]", "device-1", isActive: true);

        var response = await _handler.HandleAsync(
            new DeregisterPushTokenCommand(UserId, Token: null, DeviceId: "device-1"));

        response.Deactivated.Should().Be(1);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.IsActive.Should().BeFalse();
    }
    [Fact]
    public async Task HandleAsync_ShouldDeactivateByToken()
    {
        var seeded = await SeedAsync(UserId, "ExponentPushToken[a]", deviceId: null, isActive: true);

        var response = await _handler.HandleAsync(
            new DeregisterPushTokenCommand(UserId, Token: "ExponentPushToken[a]", DeviceId: null));

        response.Deactivated.Should().Be(1);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldNotTouchAnotherUsersRow()
    {
        // Same device id, different owner: the caller must only ever deactivate their own row.
        var mine = await SeedAsync(UserId, "ExponentPushToken[mine]", "shared-device", isActive: true);
        var theirs = await SeedAsync(OtherUserId, "ExponentPushToken[theirs]", "shared-device", isActive: true);

        var response = await _handler.HandleAsync(
            new DeregisterPushTokenCommand(UserId, Token: null, DeviceId: "shared-device"));

        response.Deactivated.Should().Be(1);
        (await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == mine.Id)).IsActive.Should().BeFalse();
        (await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == theirs.Id)).IsActive.Should().BeTrue();
    }
}
