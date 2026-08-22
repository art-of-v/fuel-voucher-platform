using FluentAssertions;
using FuelFlow.API.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.DeleteUser;
using FuelFlow.Features.Auth.Logout;
using FuelFlow.Features.Auth.Refresh;
using FuelFlow.Features.Auth.RegisterDevice;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

public sealed class AuthCommandHandlersTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid OtherUserId = Guid.Parse("ffffffff-1111-2222-3333-444444444444");

    private readonly ApplicationDbContext _context;

    public AuthCommandHandlersTests()
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

    [Fact]
    public async Task DeleteUser_ShouldSoftDeleteUserAndRevokeTokens()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1
        };

        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DeviceId = "device-1",
            PublicKey = "public-key",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Token = "refresh-token-1",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.Devices.Add(device);
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        var handler = new DeleteUserCommandHandler(_context, new Mock<ILogger<DeleteUserCommandHandler>>().Object, new ProviderEventService(_context));
        var command = new DeleteUserCommand(UserId);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();

        var updatedUser = await _context.Users.FindAsync(UserId);
        updatedUser!.IsDeleted.Should().BeTrue();
        updatedUser.IsActive.Should().BeFalse();
        updatedUser.TokenVersion.Should().Be(2);

        var updatedDevice = await _context.Devices.FindAsync(device.Id);
        updatedDevice!.Status.Should().Be(DeviceStatus.Revoked);

        var updatedToken = await _context.RefreshTokens.FindAsync(refreshToken.Id);
        updatedToken!.IsRevoked.Should().BeTrue();
        updatedToken.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteUser_ShouldReturnError_WhenUserNotFound()
    {
        var handler = new DeleteUserCommandHandler(_context, new Mock<ILogger<DeleteUserCommandHandler>>().Object, new ProviderEventService(_context));
        var command = new DeleteUserCommand(OtherUserId);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("User not found");
    }

    [Fact]
    public async Task RegisterDevice_ShouldCreateNewDevice()
    {
        var handler = new RegisterDeviceCommandHandler(_context, new Mock<ILogger<RegisterDeviceCommandHandler>>().Object);
        var command = new RegisterDeviceCommand
        {
            UserId = UserId,
            DeviceId = "device-abc",
            PublicKey = "public-key-1",
            DeviceModel = "iPhone 15",
            OsVersion = "iOS 18",
            AppVersion = "1.0.0"
        };

        var response = await handler.HandleAsync(command);

        response.DeviceId.Should().Be("device-abc");
        response.Status.Should().Be("Active");
        response.DeviceIdGuid.Should().NotBeEmpty();

        var stored = await _context.Devices.SingleAsync(d => d.DeviceId == "device-abc");
        stored.Id.Should().Be(response.DeviceIdGuid);
        stored.UserId.Should().Be(UserId);
        stored.PublicKey.Should().Be("public-key-1");
        stored.DeviceModel.Should().Be("iPhone 15");
        stored.OsVersion.Should().Be("iOS 18");
        stored.AppVersion.Should().Be("1.0.0");
        stored.Status.Should().Be(DeviceStatus.Active);
    }

    [Fact]
    public async Task RegisterDevice_ShouldUpdateExistingDevice()
    {
        var existingDevice = new Device
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DeviceId = "device-abc",
            PublicKey = "old-key",
            DeviceModel = "Old Model",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.Devices.Add(existingDevice);
        await _context.SaveChangesAsync();

        var handler = new RegisterDeviceCommandHandler(_context, new Mock<ILogger<RegisterDeviceCommandHandler>>().Object);
        var command = new RegisterDeviceCommand
        {
            UserId = UserId,
            DeviceId = "device-abc",
            PublicKey = "new-key",
            DeviceModel = "New Model",
            OsVersion = "Android 15",
            AppVersion = "2.0.0"
        };

        var response = await handler.HandleAsync(command);

        response.DeviceIdGuid.Should().Be(existingDevice.Id);
        response.Status.Should().Be("Active");

        _context.Devices.Should().HaveCount(1);

        var stored = await _context.Devices.FindAsync(existingDevice.Id);
        stored!.PublicKey.Should().Be("new-key");
        stored.DeviceModel.Should().Be("New Model");
        stored.OsVersion.Should().Be("Android 15");
        stored.AppVersion.Should().Be("2.0.0");
        stored.Status.Should().Be(DeviceStatus.Active);
        stored.UserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Logout_ShouldRevokeDeviceMatchingUserIdAndDeviceId()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DeviceId = "device-abc",
            PublicKey = "public-key",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        var handler = new LogoutDeviceCommandHandler(_context, new Mock<ILogger<LogoutDeviceCommandHandler>>().Object);
        var command = new LogoutDeviceCommand("device-abc", UserId);

        await handler.HandleAsync(command, CancellationToken.None);

        var stored = await _context.Devices.FindAsync(device.Id);
        stored!.Status.Should().Be(DeviceStatus.Revoked);
    }

    [Fact]
    public async Task Logout_ShouldNotRevokeDevice_WhenUserIdDoesNotMatch()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = OtherUserId,
            DeviceId = "device-abc",
            PublicKey = "public-key",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        var handler = new LogoutDeviceCommandHandler(_context, new Mock<ILogger<LogoutDeviceCommandHandler>>().Object);
        var command = new LogoutDeviceCommand("device-abc", UserId);

        await handler.HandleAsync(command, CancellationToken.None);

        var stored = await _context.Devices.FindAsync(device.Id);
        stored!.Status.Should().Be(DeviceStatus.Active);
    }

    [Fact]
    public async Task SendCode_ShouldPersistVerificationCode_WithDevBypass()
    {
        var smsServiceMock = new Mock<ISmsService>();
        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authOptionsMock = new Mock<IOptions<AuthOptions>>();
        authOptionsMock.Setup(o => o.Value).Returns(new AuthOptions { DevBypass = true });

        var handler = new SendCodeCommandHandler(
            _context,
            smsServiceMock.Object,
            phoneNumberServiceMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authOptionsMock.Object);

        var response = await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        response.Success.Should().BeTrue();

        var storedCode = await _context.VerificationCodes
            .SingleOrDefaultAsync(v => v.PhoneNumber == "+380991234567");
        storedCode.Should().NotBeNull();
        storedCode!.Code.Should().Be(SecretsHasher.Hash("000000"));
        storedCode.IsUsed.Should().BeFalse();

        smsServiceMock.Verify(
            x => x.SendVerificationCodeAsync("+380991234567", "000000", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendCode_ShouldSendSms_WhenNotDevBypass()
    {
        var smsServiceMock = new Mock<ISmsService>();
        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authOptionsMock = new Mock<IOptions<AuthOptions>>();
        authOptionsMock.Setup(o => o.Value).Returns(new AuthOptions { DevBypass = false });

        var handler = new SendCodeCommandHandler(
            _context,
            smsServiceMock.Object,
            phoneNumberServiceMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authOptionsMock.Object);

        var response = await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        response.Success.Should().BeTrue();

        smsServiceMock.Verify(
            x => x.SendVerificationCodeAsync("+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var storedCode = await _context.VerificationCodes
            .SingleAsync(v => v.PhoneNumber == "+380991234567");
        storedCode.Code.Should().MatchRegex("^[0-9A-F]{64}$");
        storedCode.Code.Should().NotBe(SecretsHasher.Hash("000000"));
    }

    [Fact]
    public async Task SendCode_ShouldUseConfiguredCode_AndSkipSms_WhenPhoneIsAllowlisted()
    {
        var smsServiceMock = new Mock<ISmsService>();
        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authOptionsMock = new Mock<IOptions<AuthOptions>>();
        authOptionsMock.Setup(o => o.Value).Returns(new AuthOptions
        {
            DevBypass = false,
            TestPhones = new Dictionary<string, string>
            {
                ["+380991234567"] = "246810"
            }
        });

        var handler = new SendCodeCommandHandler(
            _context,
            smsServiceMock.Object,
            phoneNumberServiceMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authOptionsMock.Object);

        var response = await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        response.Success.Should().BeTrue();

        var storedCode = await _context.VerificationCodes
            .SingleAsync(v => v.PhoneNumber == "+380991234567");
        storedCode.Code.Should().Be(SecretsHasher.Hash("246810"));

        // Allowlisted QA numbers must never hit the SMS provider.
        smsServiceMock.Verify(
            x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_ShouldPreferDevBypass_OverTestPhoneAllowlist()
    {
        var smsServiceMock = new Mock<ISmsService>();
        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authOptionsMock = new Mock<IOptions<AuthOptions>>();
        authOptionsMock.Setup(o => o.Value).Returns(new AuthOptions
        {
            DevBypass = true,
            TestPhones = new Dictionary<string, string>
            {
                ["+380991234567"] = "246810"
            }
        });

        var handler = new SendCodeCommandHandler(
            _context,
            smsServiceMock.Object,
            phoneNumberServiceMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authOptionsMock.Object);

        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        var storedCode = await _context.VerificationCodes
            .SingleAsync(v => v.PhoneNumber == "+380991234567");
        storedCode.Code.Should().Be(SecretsHasher.Hash("000000"));
    }

    [Fact]
    public async Task Refresh_ShouldRotateToken_WhenValid()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            TokenVersion = 1
        };

        var oldToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            // Stored hashed, exactly as the issue path writes it. The handler looks up
            // by hash only - there is no raw-value fallback - so seeding the raw string
            // would make this test pass through the "token not found" branch instead of
            // the rotation branch it is meant to cover.
            Token = SecretsHasher.Hash("old-refresh-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(oldToken);
        await _context.SaveChangesAsync();

        var tokenServiceMock = new Mock<IJwtTokenService>();
        tokenServiceMock
            .Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("new-refresh-token");

        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions
        {
            Secret = "secret",
            Issuer = "issuer",
            Audience = "audience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var response = await handler.HandleAsync(new RefreshTokenCommand("old-refresh-token"), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("new-refresh-token");
        response.ExpiresIn.Should().Be(900);

        var revokedOld = await _context.RefreshTokens.FindAsync(oldToken.Id);
        revokedOld!.IsRevoked.Should().BeTrue();
        revokedOld.RevokedAtUtc.Should().NotBeNull();

        var newStored = await _context.RefreshTokens.SingleAsync(rt => rt.Token == SecretsHasher.Hash("new-refresh-token"));
        newStored.UserId.Should().Be(UserId);
        newStored.IsRevoked.Should().BeFalse();

        tokenServiceMock.Verify(
            x => x.GenerateAccessToken(UserId, "+380991234567", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), 1),
            Times.Once);
    }

    [Fact]
    public async Task Refresh_ShouldThrow_WhenTokenNotFound()
    {
        var tokenServiceMock = new Mock<IJwtTokenService>();
        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions());

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var act = async () => await handler.HandleAsync(new RefreshTokenCommand("missing-token"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Refresh_ShouldThrow_WhenTokenExpired()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            TokenVersion = 1
        };

        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            // Hashed: a raw seed is unfindable and the test would pass on the
            // "not found" branch without ever reaching the expiry check.
            Token = SecretsHasher.Hash("expired-refresh-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(expiredToken);
        await _context.SaveChangesAsync();

        var tokenServiceMock = new Mock<IJwtTokenService>();
        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions());

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var act = async () => await handler.HandleAsync(new RefreshTokenCommand("expired-refresh-token"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired refresh token");
    }

    [Fact]
    public async Task Refresh_ShouldRevokeWholeFamily_WhenRevokedTokenIsReplayed()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            TokenVersion = 1
        };

        var family = Guid.NewGuid();
        var otherFamily = Guid.NewGuid();

        // Stolen token: already rotated away (revoked).
        var stolen = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = family,
            Token = SecretsHasher.Hash("stolen-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow,
            User = user
        };

        // The legitimate current token of the same family.
        var current = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = family,
            Token = SecretsHasher.Hash("current-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        // A token from another login session (other device) - must survive.
        var otherDevice = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = otherFamily,
            Token = SecretsHasher.Hash("other-device-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.RefreshTokens.AddRange(stolen, current, otherDevice);
        await _context.SaveChangesAsync();

        var tokenServiceMock = new Mock<IJwtTokenService>();
        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions());

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var act = async () => await handler.HandleAsync(new RefreshTokenCommand("stolen-token"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired refresh token");

        // Whole family is dead, the other device session is untouched.
        (await _context.RefreshTokens.SingleAsync(rt => rt.Token == SecretsHasher.Hash("current-token"))).IsRevoked.Should().BeTrue();
        (await _context.RefreshTokens.SingleAsync(rt => rt.Token == SecretsHasher.Hash("other-device-token"))).IsRevoked.Should().BeFalse();
        tokenServiceMock.Verify(x => x.GenerateRefreshToken(), Times.Never);
    }

    [Fact]
    public async Task Refresh_ShouldKeepFamilyId_WhenRotating()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            TokenVersion = 1
        };

        var family = Guid.NewGuid();
        var oldToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = family,
            Token = SecretsHasher.Hash("rotatable-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.RefreshTokens.Add(oldToken);
        await _context.SaveChangesAsync();

        var tokenServiceMock = new Mock<IJwtTokenService>();
        tokenServiceMock
            .Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("rotated-token");

        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions
        {
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        await handler.HandleAsync(new RefreshTokenCommand("rotatable-token"), CancellationToken.None);

        var rotated = await _context.RefreshTokens.SingleAsync(rt => rt.Token == SecretsHasher.Hash("rotated-token"));
        rotated.FamilyId.Should().Be(family, "rotation must stay inside the same family for reuse detection to work");
    }

    [Fact]
    public async Task Refresh_ShouldRevokeAllUserTokens_WhenLegacyTokenIsReplayed()
    {
        var user = new User
        {
            Id = UserId,
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            TokenVersion = 1
        };

        // Legacy token: issued before families existed (empty FamilyId).
        var legacyStolen = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = Guid.Empty,
            Token = SecretsHasher.Hash("legacy-stolen-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = true,
            RevokedAtUtc = DateTime.UtcNow,
            User = user
        };

        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            FamilyId = Guid.NewGuid(),
            Token = SecretsHasher.Hash("active-token"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.RefreshTokens.AddRange(legacyStolen, activeToken);
        await _context.SaveChangesAsync();

        var tokenServiceMock = new Mock<IJwtTokenService>();
        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions());

        var handler = new RefreshTokenCommandHandler(
            _context,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var act = async () => await handler.HandleAsync(new RefreshTokenCommand("legacy-stolen-token"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        // Legacy reuse cannot be scoped to a family, so every session dies.
        (await _context.RefreshTokens.SingleAsync(rt => rt.Token == SecretsHasher.Hash("active-token"))).IsRevoked.Should().BeTrue();
    }
}
