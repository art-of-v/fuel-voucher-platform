using FluentAssertions;
using FuelFlow.Features.Notifications.ParkPushToken;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Notifications;

/// <summary>
/// Logging out must not take the device's ability to RECEIVE THE SIGN-IN CODE with it.
/// </summary>
/// <remarks>
/// The logout used to deactivate the push token, and a deactivated token is not an audience:
/// the next sign-in code had no device to go to and fell back to the paid SMS channel - the
/// channel the push channel exists to avoid, and the one whose budget was already gone. So a
/// logout parks the token instead: the device stops receiving the platform's business
/// notifications, and stays addressable for the code that signs it back in.
/// </remarks>
public sealed class ParkPushTokenCommandHandlerTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;
    private readonly ParkPushTokenCommandHandler _handler;

    public ParkPushTokenCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new ParkPushTokenCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<UserPushToken> SeedAsync(Guid userId, string token, string? deviceId, bool isActive)
    {
        var now = TestClock.UtcNow;
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
            new ParkPushTokenCommand(UserId, Token: null, DeviceId: null));

        response.Parked.Should().Be(0);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.AuthOtpOnly.Should().BeFalse();
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldParkByDeviceId_AndKeepTheTokenAlive()
    {
        var seeded = await SeedAsync(UserId, "ExponentPushToken[a]", "device-1", isActive: true);

        var response = await _handler.HandleAsync(
            new ParkPushTokenCommand(UserId, Token: null, DeviceId: "device-1"));

        response.Parked.Should().Be(1);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.AuthOtpOnly.Should().BeTrue();
        // Alive: the token is still a device the platform can push to.
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldParkByToken()
    {
        var seeded = await SeedAsync(UserId, "ExponentPushToken[a]", deviceId: null, isActive: true);

        var response = await _handler.HandleAsync(
            new ParkPushTokenCommand(UserId, Token: "ExponentPushToken[a]", DeviceId: null));

        response.Parked.Should().Be(1);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.AuthOtpOnly.Should().BeTrue();
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldNotTouchAnotherUsersRow()
    {
        // Same device id, different owner: the caller must only ever park their own row.
        var mine = await SeedAsync(UserId, "ExponentPushToken[mine]", "shared-device", isActive: true);
        var theirs = await SeedAsync(OtherUserId, "ExponentPushToken[theirs]", "shared-device", isActive: true);

        var response = await _handler.HandleAsync(
            new ParkPushTokenCommand(UserId, Token: null, DeviceId: "shared-device"));

        response.Parked.Should().Be(1);
        (await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == mine.Id)).AuthOtpOnly.Should().BeTrue();
        (await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == theirs.Id)).AuthOtpOnly.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldLeaveADeadTokenDead()
    {
        // A token Expo reported as gone is a phone that is not there any more. Parking it would
        // say "the sign-in code may go here" about a device that cannot receive anything.
        var seeded = await SeedAsync(UserId, "ExponentPushToken[dead]", "device-1", isActive: false);

        var response = await _handler.HandleAsync(
            new ParkPushTokenCommand(UserId, Token: null, DeviceId: "device-1"));

        response.Parked.Should().Be(0);
        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == seeded.Id);
        row.AuthOtpOnly.Should().BeFalse();
        row.IsActive.Should().BeFalse();
    }
}
