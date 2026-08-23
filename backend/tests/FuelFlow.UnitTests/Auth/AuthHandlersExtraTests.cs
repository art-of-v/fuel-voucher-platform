using System.Security.Cryptography;
using FluentAssertions;
using FuelFlow.API.Features.Auth.GenerateChallenge;
using FuelFlow.API.Features.Auth.VerifyChallenge;
using FuelFlow.Features.Auth.GenerateChallenge;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Auth.VerifyChallenge;
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

public sealed class AuthHandlersExtraTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public AuthHandlersExtraTests()
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

    // --- VerifyCodeCommandHandler ----------------------------------------

    private VerifyCodeCommandHandler BuildVerifyCodeHandler()
    {
        var tokenServiceMock = new Mock<IJwtTokenService>();
        tokenServiceMock
            .Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock
            .Setup(x => x.Normalize(It.IsAny<string>()))
            .Returns((string phone) => phone);

        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions
        {
            Secret = "secret",
            Issuer = "issuer",
            Audience = "audience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        return new VerifyCodeCommandHandler(
            _context,
            tokenServiceMock.Object,
            phoneNumberServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<VerifyCodeCommandHandler>>().Object,
            new ProviderEventService(_context));
    }

    [Fact]
    public async Task VerifyCode_ShouldCreateUser_WhenNotExists()
    {
        var code = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };
        _context.VerificationCodes.Add(code);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var response = await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");
        response.ExpiresIn.Should().Be(900);

        var user = await _context.Users.SingleAsync();
        user.PhoneNumber.Should().Be("+380991234567");
        user.IsActive.Should().BeTrue();
        user.IsDeleted.Should().BeFalse();

        var storedCode = await _context.VerificationCodes.FindAsync(code.Id);
        storedCode!.IsUsed.Should().BeTrue();
        storedCode.UsedAtUtc.Should().NotBeNull();

        var storedRefresh = await _context.RefreshTokens.SingleAsync();
        storedRefresh.Token.Should().Be(SecretsHasher.Hash("refresh-token"));
        storedRefresh.UserId.Should().Be(user.Id);
        storedRefresh.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCode_ShouldLoginExistingUser()
    {
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-5),
            IsActive = true,
            IsDeleted = false,
            TokenVersion = 1
        };
        _context.Users.Add(existingUser);

        var code = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };
        _context.VerificationCodes.Add(code);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var response = await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        response.RefreshToken.Should().Be("refresh-token");

        var users = await _context.Users.ToListAsync();
        users.Should().ContainSingle();
        var user = users[0];
        user.Id.Should().Be(existingUser.Id);
        user.TokenVersion.Should().Be(1);
        user.LastLoginAtUtc.Should().NotBeNull();

        var storedCode = await _context.VerificationCodes.FindAsync(code.Id);
        storedCode!.IsUsed.Should().BeTrue();

        var storedRefresh = await _context.RefreshTokens.SingleAsync();
        storedRefresh.UserId.Should().Be(existingUser.Id);
    }

    [Fact]
    public async Task VerifyCode_ShouldThrow_WhenCodeInvalid()
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("000000"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        });
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "999999"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");
    }

    [Fact]
    public async Task VerifyCode_ShouldThrow_WhenCodeUsed()
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = true
        });
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");
    }

    [Fact]
    public async Task VerifyCode_ShouldThrow_WhenCodeExpired()
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            IsUsed = false
        });
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");
    }

    [Fact]
    public async Task VerifyCode_ShouldCountFailedAttempt_WhenCodeWrong()
    {
        var code = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };
        _context.VerificationCodes.Add(code);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "999999"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var storedCode = await _context.VerificationCodes.FindAsync(code.Id);
        storedCode!.FailedAttempts.Should().Be(1);
        storedCode.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCode_ShouldInvalidateCode_AfterMaxFailedAttempts()
    {
        var code = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };
        _context.VerificationCodes.Add(code);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();
        for (var attempt = 1; attempt <= VerifyCodeCommandHandler.MaxFailedAttempts; attempt++)
        {
            var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "999999"), CancellationToken.None);
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // The code is now invalidated: even the correct code must be rejected.
        var finalAct = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);
        await finalAct.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");

        var storedCode = await _context.VerificationCodes.FindAsync(code.Id);
        storedCode!.FailedAttempts.Should().Be(VerifyCodeCommandHandler.MaxFailedAttempts);
        storedCode.IsUsed.Should().BeTrue();
        storedCode.UsedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyCode_ShouldSucceed_AfterEarlierFailedAttempts_WhenCodeCorrect()
    {
        var code = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            Code = SecretsHasher.Hash("123456"),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        };
        _context.VerificationCodes.Add(code);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyCodeHandler();

        var act = async () => await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "999999"), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var response = await handler.HandleAsync(new VerifyCodeCommand("+380991234567", "123456"), CancellationToken.None);
        response.AccessToken.Should().Be("access-token");

        var storedCode = await _context.VerificationCodes.FindAsync(code.Id);
        storedCode!.IsUsed.Should().BeTrue();
    }

    // --- GenerateChallengeCommandHandler ----------------------------------

    private GenerateChallengeCommandHandler BuildGenerateChallengeHandler(ICacheService cacheService)
    {
        var optionsMock = new Mock<IOptions<DeviceAuthOptions>>();
        optionsMock.Setup(o => o.Value).Returns(new DeviceAuthOptions { ChallengeExpirySeconds = 30 });

        return new GenerateChallengeCommandHandler(
            _context,
            cacheService,
            optionsMock.Object,
            new Mock<ILogger<GenerateChallengeCommandHandler>>().Object);
    }

    private async Task<Device> SeedActiveDeviceAsync(string deviceId, string? publicKey = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = $"+38099{Guid.NewGuid().ToString("N")[..7]}",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false
        };
        _context.Users.Add(user);

        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = deviceId,
            PublicKey = publicKey ?? "public-key",
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        _context.Devices.Add(device);
        await _context.SaveChangesAsync();

        return device;
    }

    [Fact]
    public async Task GenerateChallenge_ShouldStoreChallengeInCache()
    {
        await SeedActiveDeviceAsync("device-1");

        var cacheServiceMock = new Mock<ICacheService>();
        var handler = BuildGenerateChallengeHandler(cacheServiceMock.Object);

        var response = await handler.HandleAsync(new GenerateChallengeCommand { DeviceId = "device-1" }, CancellationToken.None);

        response.Challenge.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(response.Challenge).Should().HaveCount(32);
        response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        response.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(5));

        // Keyed by the challenge value, not by device alone, so a second challenge request
        // cannot displace an outstanding one.
        cacheServiceMock.Verify(
            x => x.SetAsync($"challenge:device-1:{response.Challenge}", It.IsAny<string>(), TimeSpan.FromSeconds(30), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateChallenge_ShouldNotStoreChallengeForUnknownDevice()
    {
        // Regression: this endpoint is anonymous and took the device_id from the body, storing a
        // Redis key for any string a caller supplied - an unauthenticated keyspace-growth lever.
        var cacheServiceMock = new Mock<ICacheService>();
        var handler = BuildGenerateChallengeHandler(cacheServiceMock.Object);

        var response = await handler.HandleAsync(new GenerateChallengeCommand { DeviceId = "never-enrolled" }, CancellationToken.None);

        cacheServiceMock.Verify(
            x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The response must be indistinguishable from the enrolled case, or this endpoint becomes
        // an oracle for "is this device_id enrolled".
        response.Challenge.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(response.Challenge).Should().HaveCount(32);
        response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GenerateChallenge_ShouldNotStoreChallengeForRevokedDevice()
    {
        var device = await SeedActiveDeviceAsync("device-revoked");
        device.Status = DeviceStatus.Revoked;
        await _context.SaveChangesAsync();

        var cacheServiceMock = new Mock<ICacheService>();
        var handler = BuildGenerateChallengeHandler(cacheServiceMock.Object);

        await handler.HandleAsync(new GenerateChallengeCommand { DeviceId = "device-revoked" }, CancellationToken.None);

        cacheServiceMock.Verify(
            x => x.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- VerifyChallengeCommandHandler ------------------------------------

    private VerifyChallengeCommandHandler BuildVerifyChallengeHandler(ICacheService? cacheService = null)
    {
        var tokenServiceMock = new Mock<IJwtTokenService>();
        tokenServiceMock
            .Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var jwtOptionsMock = new Mock<IOptions<JwtOptions>>();
        jwtOptionsMock.Setup(o => o.Value).Returns(new JwtOptions
        {
            Secret = "secret",
            Issuer = "issuer",
            Audience = "audience",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        return new VerifyChallengeCommandHandler(
            _context,
            cacheService ?? new Mock<ICacheService>().Object,
            tokenServiceMock.Object,
            jwtOptionsMock.Object,
            new Mock<ILogger<VerifyChallengeCommandHandler>>().Object);
    }

    [Fact]
    public async Task VerifyChallenge_ShouldReturnInvalid_WhenChallengeNotInCache()
    {
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = BuildVerifyChallengeHandler(cacheServiceMock.Object);
        var response = await handler.HandleAsync(
            new VerifyChallengeCommand { DeviceId = "device-1", Challenge = "challenge", Signature = "signature" },
            CancellationToken.None);

        response.IsValid.Should().BeFalse();
        response.Error.Should().Be("Challenge not found or expired");
    }

    [Fact]
    public async Task VerifyChallenge_ShouldReturnInvalid_WhenChallengeWasNeverIssued()
    {
        // The cache is now keyed by the challenge value, so a challenge this server never issued
        // is simply an absent key. That collapses the old distinct "Invalid challenge" reply into
        // the same answer an expired challenge gets - one less thing for a caller to distinguish.
        // No client branches on either string (checked across mobile/src and admin/src).
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.ExistsAsync("challenge:device-1:issued-challenge", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = BuildVerifyChallengeHandler(cacheServiceMock.Object);
        var response = await handler.HandleAsync(
            new VerifyChallengeCommand { DeviceId = "device-1", Challenge = "different-challenge", Signature = "signature" },
            CancellationToken.None);

        response.IsValid.Should().BeFalse();
        response.Error.Should().Be("Challenge not found or expired");
    }

    [Fact]
    public async Task VerifyChallenge_ShouldReturnInvalid_WhenDeviceNotFound()
    {
        var challenge = "challenge-value";
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.ExistsAsync($"challenge:device-1:{challenge}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = BuildVerifyChallengeHandler(cacheServiceMock.Object);
        var response = await handler.HandleAsync(
            new VerifyChallengeCommand { DeviceId = "device-1", Challenge = challenge, Signature = "signature" },
            CancellationToken.None);

        response.IsValid.Should().BeFalse();
        response.Error.Should().Be("Device not found or revoked");
    }

    [Fact]
    public async Task VerifyChallenge_ShouldReturnInvalid_WhenSignatureMalformed()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+380991234567",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true,
            IsDeleted = false
        };
        _context.Users.Add(user);

        using var rsa = RSA.Create(2048);
        _context.Devices.Add(new Device
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DeviceId = "device-1",
            PublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            Status = DeviceStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var challenge = "challenge-value";
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.ExistsAsync($"challenge:device-1:{challenge}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = BuildVerifyChallengeHandler(cacheServiceMock.Object);
        var response = await handler.HandleAsync(
            new VerifyChallengeCommand { DeviceId = "device-1", Challenge = challenge, Signature = Convert.ToBase64String(new byte[256]) },
            CancellationToken.None);

        response.IsValid.Should().BeFalse();
        response.Error.Should().Be("Invalid signature");
    }

    [Fact]
    public async Task VerifyChallenge_ShouldRejectNonBase64Signature_WithoutThrowing()
    {
        // Regression: VerifySignature called Convert.FromBase64String on the client-supplied
        // signature outside any try/catch, so "!!!not-base64!!!" produced an unhandled
        // FormatException - a 500 plus an error-log row on an anonymous endpoint, instead of a 401.
        var device = await SeedActiveDeviceAsync("device-b64", RSA.Create(2048).ExportSubjectPublicKeyInfoPem());

        var challenge = "challenge-value";
        var cacheServiceMock = new Mock<ICacheService>();
        cacheServiceMock
            .Setup(x => x.ExistsAsync($"challenge:{device.DeviceId}:{challenge}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = BuildVerifyChallengeHandler(cacheServiceMock.Object);

        var act = async () => await handler.HandleAsync(
            new VerifyChallengeCommand { DeviceId = device.DeviceId, Challenge = challenge, Signature = "!!!not-base64!!!" },
            CancellationToken.None);

        var response = await act.Should().NotThrowAsync();
        response.Subject.IsValid.Should().BeFalse();
        response.Subject.Error.Should().Be("Invalid signature");
    }
}
