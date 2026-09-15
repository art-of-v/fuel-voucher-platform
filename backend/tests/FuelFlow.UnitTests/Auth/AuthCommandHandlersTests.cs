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
using FuelFlow.SharedKernel.Notifications.Email;
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
        var handler = new RegisterDeviceCommandHandler(_context, null!, new Mock<ILogger<RegisterDeviceCommandHandler>>().Object);
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

        var handler = new RegisterDeviceCommandHandler(_context, null!, new Mock<ILogger<RegisterDeviceCommandHandler>>().Object);
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
    public async Task RegisterDevice_ShouldRefuseDeviceOwnedByAnotherUser()
    {
        // Regression: the handler matched on DeviceId alone and then overwrote UserId and
        // PublicKey, so any authenticated caller who learned another account's device_id
        // could seize the row - destroying the victim's public key, locking them out of
        // device-signed purchases and of the challenge/verify re-auth path, and flipping a
        // Revoked device back to Active. device_id is not a secret: it travels as the
        // x-device-id header on every request and is logged at Information level.
        // RegisterDevice_ShouldUpdateExistingDevice above passes the SAME UserId twice, so
        // it only ever covered legitimate same-user key rotation - the cross-user case had
        // no test at all, which is why the flaw survived.
        var victimDevice = new Device
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            DeviceId = "device-abc",
            PublicKey = "victim-key",
            DeviceModel = "Victim Phone",
            Status = DeviceStatus.Revoked,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            LastSeenAt = DateTime.UtcNow.AddDays(-1)
        };

        _context.Devices.Add(victimDevice);
        await _context.SaveChangesAsync();

        var handler = new RegisterDeviceCommandHandler(_context, null!, new Mock<ILogger<RegisterDeviceCommandHandler>>().Object);
        var command = new RegisterDeviceCommand
        {
            UserId = OtherUserId,
            DeviceId = "device-abc",
            PublicKey = "attacker-key",
            DeviceModel = "Attacker Phone",
            OsVersion = "Android 15",
            AppVersion = "2.0.0"
        };

        var response = await handler.HandleAsync(command);

        // The controller maps a non-null Error to 409 Conflict.
        response.Error.Should().Be("DeviceAlreadyRegistered");
        response.DeviceIdGuid.Should().BeEmpty();

        // Nothing about the victim's row may change - key, owner, or status.
        var stored = await _context.Devices.FindAsync(victimDevice.Id);
        stored!.UserId.Should().Be(UserId);
        stored.PublicKey.Should().Be("victim-key");
        stored.DeviceModel.Should().Be("Victim Phone");
        stored.Status.Should().Be(DeviceStatus.Revoked);
        _context.Devices.Should().HaveCount(1);
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
    public async Task Logout_ShouldRevokeRefreshTokensAndBumpTokenVersion()
    {
        // Regression: logout revoked only the device row, leaving every issued credential
        // live. The access token stayed valid to expiry and the refresh token stayed valid
        // for RefreshTokenExpirationDays, rotating into a fresh 7-day token on each use
        // (RefreshTokenCommand.cs:114) because that handler never checks TokenVersion. A
        // one-time token capture therefore survived the victim tapping "log out" - the only
        // remedy was deleting the account.
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
            DeviceId = "device-abc",
            PublicKey = "public-key",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        var activeToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Token = "refresh-token-live",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = user
        };

        _context.Users.Add(user);
        _context.Devices.Add(device);
        _context.RefreshTokens.Add(activeToken);
        await _context.SaveChangesAsync();

        var handler = new LogoutDeviceCommandHandler(_context, new Mock<ILogger<LogoutDeviceCommandHandler>>().Object);

        await handler.HandleAsync(new LogoutDeviceCommand("device-abc", UserId), CancellationToken.None);

        var storedToken = await _context.RefreshTokens.FindAsync(activeToken.Id);
        storedToken!.IsRevoked.Should().BeTrue();
        storedToken.RevokedAtUtc.Should().NotBeNull();

        // Invalidates already-issued access tokens via SessionValidationMiddleware.
        var storedUser = await _context.Users.FindAsync(UserId);
        storedUser!.TokenVersion.Should().Be(2);

        var storedDevice = await _context.Devices.FindAsync(device.Id);
        storedDevice!.Status.Should().Be(DeviceStatus.Revoked);
    }

    [Fact]
    public async Task Logout_ShouldNotTouchAnotherUsersCredentials()
    {
        // The device lookup is scoped by UserId, but the credential revocation is scoped by
        // the UserId claim, so confirm a logout cannot be aimed at a second account.
        var otherUser = new User
        {
            Id = OtherUserId,
            PhoneNumber = "+380997654321",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 3
        };

        var otherToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = OtherUserId,
            Token = "other-refresh-token",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false,
            User = otherUser
        };

        _context.Users.Add(otherUser);
        _context.RefreshTokens.Add(otherToken);
        await _context.SaveChangesAsync();

        var handler = new LogoutDeviceCommandHandler(_context, new Mock<ILogger<LogoutDeviceCommandHandler>>().Object);

        await handler.HandleAsync(new LogoutDeviceCommand("device-abc", UserId), CancellationToken.None);

        var storedToken = await _context.RefreshTokens.FindAsync(otherToken.Id);
        storedToken!.IsRevoked.Should().BeFalse();

        var storedUser = await _context.Users.FindAsync(OtherUserId);
        storedUser!.TokenVersion.Should().Be(3);
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
            authOptionsMock.Object,
            UnconfiguredEmailSender());

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
            authOptionsMock.Object,
            UnconfiguredEmailSender());

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

    private static IEmailSender UnconfiguredEmailSender()
    {
        var mock = new Mock<IEmailSender>();
        mock.SetupGet(e => e.IsConfigured).Returns(false);
        return mock.Object;
    }

    private SendCodeCommandHandler BuildSendCodeHandler(
        Mock<ISmsService> smsMock, IEmailSender emailSender, bool adminOtpViaEmail = true)
    {
        var phoneMock = new Mock<IPhoneNumberService>();
        phoneMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);
        var authMock = new Mock<IOptions<AuthOptions>>();
        authMock.Setup(o => o.Value).Returns(new AuthOptions { DevBypass = false, AdminOtpViaEmail = adminOtpViaEmail });
        return new SendCodeCommandHandler(
            _context, smsMock.Object, phoneMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object, authMock.Object, emailSender);
    }

    private async Task SeedUserWithRole(string phone, string roleName, string? email)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = DateTime.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Email = email,
            RoleId = role.Id,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task SendCode_AdminWithConfiguredEmail_EmailsCode_AndDoesNotSms()
    {
        await SeedUserWithRole("+380991234567", AuthOptions.AdminRoleName, "admin@palne.shop");
        var smsMock = new Mock<ISmsService>();

        string? sentTo = null;
        string? sentBody = null;
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(true);
        emailMock
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string body, CancellationToken _) => { sentTo = to; sentBody = body; })
            .Returns(Task.CompletedTask);

        var handler = BuildSendCodeHandler(smsMock, emailMock.Object);
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        sentTo.Should().Be("admin@palne.shop");
        sentBody.Should().MatchRegex(@"code is \d{6}\.");
        smsMock.Verify(x => x.SendVerificationCodeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCode_NonAdminUser_SendsSmsNotEmail()
    {
        await SeedUserWithRole("+380991234567", "Customer", "user@palne.shop");
        var smsMock = new Mock<ISmsService>();
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(true);

        var handler = BuildSendCodeHandler(smsMock, emailMock.Object);
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        smsMock.Verify(x => x.SendVerificationCodeAsync(
            "+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        emailMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCode_AdminWithoutEmail_FallsBackToSms()
    {
        await SeedUserWithRole("+380991234567", AuthOptions.AdminRoleName, null);
        var smsMock = new Mock<ISmsService>();
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(true);

        var handler = BuildSendCodeHandler(smsMock, emailMock.Object);
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        smsMock.Verify(x => x.SendVerificationCodeAsync(
            "+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCode_EmailNotConfigured_AdminFallsBackToSms()
    {
        await SeedUserWithRole("+380991234567", AuthOptions.AdminRoleName, "admin@palne.shop");
        var smsMock = new Mock<ISmsService>();

        var handler = BuildSendCodeHandler(smsMock, UnconfiguredEmailSender());
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        smsMock.Verify(x => x.SendVerificationCodeAsync(
            "+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCode_AdminEmailSendThrows_FallsBackToSms()
    {
        await SeedUserWithRole("+380991234567", AuthOptions.AdminRoleName, "admin@palne.shop");
        var smsMock = new Mock<ISmsService>();
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(true);
        emailMock
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var handler = BuildSendCodeHandler(smsMock, emailMock.Object);
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        smsMock.Verify(x => x.SendVerificationCodeAsync(
            "+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCode_AdminOtpViaEmailDisabled_UsesSmsEvenForAdmin()
    {
        await SeedUserWithRole("+380991234567", AuthOptions.AdminRoleName, "admin@palne.shop");
        var smsMock = new Mock<ISmsService>();
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(true);

        var handler = BuildSendCodeHandler(smsMock, emailMock.Object, adminOtpViaEmail: false);
        await handler.HandleAsync(new SendCodeCommand("+380991234567"), CancellationToken.None);

        smsMock.Verify(x => x.SendVerificationCodeAsync(
            "+380991234567", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        emailMock.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
