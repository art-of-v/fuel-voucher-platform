using FluentAssertions;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.SharedServices;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using FuelFlow.UnitTests.Shared;
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

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = OriginalOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.RsaSecondary,
            Status = DeviceStatus.Revoked,
            CreatedAt = TestClock.UtcNow.AddDays(-1),
            LastSeenAt = TestClock.UtcNow.AddDays(-1)
        });
        _context.Users.Add(new FuelFlow.SharedKernel.Domain.User
        {
            Id = OriginalOwner,
            PhoneNumber = "+380000000001",
            CreatedAtUtc = TestClock.UtcNow.AddDays(-1)
        });
        _context.Users.Add(new FuelFlow.SharedKernel.Domain.User
        {
            Id = NewOwner,
            PhoneNumber = "+380000000002",
            CreatedAtUtc = TestClock.UtcNow
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
    public async Task NewDevice_PinsTheAlgorithmInferredFromTheKey_AndVersionOneByDefault()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = "device-fresh-1",
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        response.Error.Should().BeNull();
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == "device-fresh-1");
        stored.SignatureAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.RsaPkcs1Sha256);
        stored.SignaturePayloadVersion.Should().Be(DeviceSignatureVerifier.Version1);
    }

    [Fact]
    public async Task NewDevice_StoresTheExplicitPinAndPayloadVersion()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = "device-fresh-2",
            PublicKey = DeviceTestKeys.EcPrimary,
            SignatureAlgorithm = "EcdsaSha256P1363",
            SignaturePayloadVersion = DeviceSignatureVerifier.Version2
        });

        response.Error.Should().BeNull();
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == "device-fresh-2");
        stored.SignatureAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.EcdsaSha256P1363);
        stored.SignaturePayloadVersion.Should().Be(DeviceSignatureVerifier.Version2);
    }

    [Fact]
    public async Task AnRsaSchemePinnedToAnEcKey_IsRefusedBeforeAnyRowIsWritten()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = "device-fresh-3",
            PublicKey = DeviceTestKeys.EcPrimary,
            SignatureAlgorithm = "RsaPkcs1Sha256",
            SignaturePayloadVersion = DeviceSignatureVerifier.Version2
        });

        response.Error.Should().Be("SignatureAlgorithmDoesNotMatchKey");
        (await _context.Devices.AnyAsync(d => d.DeviceId == "device-fresh-3")).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownPayloadVersion_IsRefused()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = "device-fresh-4",
            PublicKey = DeviceTestKeys.RsaPrimary,
            SignaturePayloadVersion = 3
        });

        response.Error.Should().Be("UnsupportedSignaturePayloadVersion");
        (await _context.Devices.AnyAsync(d => d.DeviceId == "device-fresh-4")).Should().BeFalse();
    }

    [Fact]
    public async Task SameUserReRegistration_RepinsTheAlgorithmAndVersion()
    {
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = OriginalOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.EcPrimary,
            SignatureAlgorithm = "EcdsaSha256Der",
            SignaturePayloadVersion = DeviceSignatureVerifier.Version2
        });

        response.Error.Should().BeNull();
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.SignatureAlgorithm.Should().Be(AsymmetricSignatureAlgorithm.EcdsaSha256Der);
        stored.SignaturePayloadVersion.Should().Be(DeviceSignatureVerifier.Version2);
    }

    [Fact]
    public async Task DifferentUser_WithoutNonce_IsRefused()
    {
        SetupNonce(false);

        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        response.Error.Should().Be("DeviceAlreadyRegistered");
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(OriginalOwner);
        stored.PublicKey.Should().Be(DeviceTestKeys.RsaSecondary);
    }

    [Fact]
    public async Task DifferentUser_WithValidNonce_RebindsDevice()
    {
        SetupNonce(true);

        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.RsaPrimary,
            RegistrationNonce = "some-otp-nonce"
        });

        response.Error.Should().BeNull();
        response.DeviceId.Should().Be(SharedDeviceId);
        response.Status.Should().Be("Active");

        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(NewOwner);
        stored.PublicKey.Should().Be(DeviceTestKeys.RsaPrimary);
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
            PublicKey = DeviceTestKeys.RsaPrimary,
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
            PublicKey = DeviceTestKeys.EcPrimary
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
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        response.Error.Should().BeNull();
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(OriginalOwner);
        stored.PublicKey.Should().Be(DeviceTestKeys.RsaPrimary);
        stored.Status.Should().Be(DeviceStatus.Active);
    }

    [Fact]
    public async Task PreviousOwnerDeleted_DeviceIsReclaimedWithoutNonce()
    {
        // The account-deletion flow revokes the device but the row keeps pointing at
        // the soft-deleted owner. A new user on the same physical device must be able
        // to reclaim it without a nonce - the deleted owner can never authenticate
        // again to release it.
        var owner = await _context.Users.SingleAsync(u => u.Id == OriginalOwner);
        owner.IsDeleted = true;
        owner.IsActive = false;
        _context.Users.Update(owner);
        await _context.SaveChangesAsync();

        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.RsaPrimary
        });

        response.Error.Should().BeNull();
        response.Status.Should().Be("Active");
        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == SharedDeviceId);
        stored.UserId.Should().Be(NewOwner);
    }

    [Fact]
    public async Task LiveOwnerRevokedDevice_StillRequiresNonce()
    {
        // A revoked device under a LIVE owner is not reclaimable without the OTP
        // nonce - revocation alone must not open the seize vector.
        var response = await CreateHandler().HandleAsync(new RegisterDeviceCommand
        {
            UserId = NewOwner,
            DeviceId = SharedDeviceId,
            PublicKey = DeviceTestKeys.EcPrimary
        });

        response.Error.Should().Be("DeviceAlreadyRegistered");
    }
}
