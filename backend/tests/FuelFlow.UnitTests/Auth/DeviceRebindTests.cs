using FluentAssertions;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace FuelFlow.UnitTests.Auth;

public sealed class DeviceRebindTests : IDisposable
{
    private static readonly Guid OriginalOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid NewOwner = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string SharedDeviceId = "8098DF00-218F-42B9-8EF1-EBDF6642F016";

    private readonly ApplicationDbContext _context;
    private readonly Mock<IDatabase> _database = new(MockBehavior.Strict);
    private readonly Mock<IConnectionMultiplexer> _redis = new(MockBehavior.Strict);

    public DeviceRebindTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = OriginalOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "old-public-key",
            Status = DeviceStatus.Revoked,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        });
        _context.SaveChanges();

        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_database.Object);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private RegisterDeviceCommandHandler CreateHandler() =>
        new(_context, _redis.Object, NullLogger<RegisterDeviceCommandHandler>.Instance);

    private void SetupNonce(bool valid)
    {
        _database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(valid);
    }

    [Fact]
    public async Task DifferentUser_WithoutNonce_IsRefused()
    {
        SetupNonce(false);

        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "new-public-key"
        });

        response.Error.Should().Be("DeviceAlreadyRegistered");
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(OriginalOwner);
        stored.PublicKey.Should().Be("old-public-key");
    }

    [Fact]
    public async Task DifferentUser_WithValidNonce_RebindsDevice()
    {
        SetupNonce(true);

        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "new-public-key",
            RegistrationNonce = "some-otp-nonce"
        });

        response.Error.Should().BeNull();
        response.DeviceId.Should().Be(SharedDeviceId);
        response.Status.Should().Be("Active");

        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(NewOwner);
        stored.PublicKey.Should().Be("new-public-key");
        stored.Status.Should().Be(DeviceStatus.Active);
    }

    [Fact]
    public async Task NonceIsSingleUse_SecondAttemptWithoutFreshNonce_IsRefused()
    {
        SetupNonce(true);
        var handler = CreateHandler();

        await handler.HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "new-public-key",
            RegistrationNonce = "nonce"
        });

        // The first KeyDeleteAsync consumed the nonce; the next attempt gets nothing.
        _database
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var second = await handler.HandleAsync(new RegisterDeviceCommand
        {
            UserId = OriginalOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "attacker-key"
        });

        second.Error.Should().Be("DeviceAlreadyRegistered");
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(NewOwner);
    }

    [Fact]
    public async Task SameUser_ReRegistration_StillAllowedWithoutNonce()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = OriginalOwner,
            DeviceId = SharedDeviceId,
            PublicKey = "recreated-biometric-key"
        });

        response.Error.Should().BeNull();
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(OriginalOwner);
        stored.PublicKey.Should().Be("recreated-biometric-key");
        stored.Status.Should().Be(DeviceStatus.Active);
    }
}
