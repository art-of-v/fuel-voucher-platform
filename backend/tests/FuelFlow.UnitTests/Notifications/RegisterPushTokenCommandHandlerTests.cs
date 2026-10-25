using FluentAssertions;
using FuelFlow.Features.Notifications.RegisterPushToken;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.UnitTests.Notifications;

/// <summary>
/// Registering the token is what a device does right after a successful sign-in, so it is
/// where a parked device gets its full audience back.
/// </summary>
public sealed class RegisterPushTokenCommandHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RegisterPushTokenCommandHandler _handler;

    public RegisterPushTokenCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _handler = new RegisterPushTokenCommandHandler(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Fact]
    public async Task HandleAsync_ShouldClearTheParkedFlag_WhenTheDeviceSignsInAgain()
    {
        // The device signed out (parked), and now it signs in: the business notifications it
        // was parked from have to come back.
        var now = TestClock.UtcNow;
        var entity = new UserPushToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = "ExponentPushToken[a]",
            Platform = "ios",
            DeviceId = "device-1",
            IsActive = true,
            AuthOtpOnly = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastSeenAtUtc = now
        };
        _context.PushTokens.Add(entity);
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(
            new RegisterPushTokenCommand(entity.UserId, entity.Token, "ios", "device-1"));

        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Id == entity.Id);
        row.AuthOtpOnly.Should().BeFalse();
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ShouldRegisterANewTokenAsAFullAudience()
    {
        var userId = Guid.NewGuid();

        await _handler.HandleAsync(
            new RegisterPushTokenCommand(userId, "ExponentPushToken[new]", "ios", "device-1"));

        var row = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Token == "ExponentPushToken[new]");
        row.AuthOtpOnly.Should().BeFalse();
        row.IsActive.Should().BeTrue();
        row.UserId.Should().Be(userId);
    }
}
