using FluentAssertions;
using FuelFlow.Features.Auth.AdminLogin;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
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

/// <summary>
/// Admin-panel login (spec §13): the staff check happens BEFORE any code is sent, and the admin
/// path never auto-registers an account. These tests pin both the security behaviour (non-staff
/// gets no code and cannot verify) and the non-enumeration behaviour (a non-staff send-code is
/// indistinguishable from a staff one).
/// </summary>
public sealed class AdminLoginHandlersTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public AdminLoginHandlersTests()
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

    // --- helpers ---------------------------------------------------------

    private static IPhoneNumberService IdentityPhone()
    {
        var mock = new Mock<IPhoneNumberService>();
        mock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string p) => p);
        return mock.Object;
    }

    private SendCodeCommandHandler BuildInnerSendCode(Mock<ISmsService> smsMock)
    {
        var authMock = new Mock<IOptions<AuthOptions>>();
        authMock.Setup(o => o.Value).Returns(new AuthOptions { DevBypass = false, AdminOtpViaEmail = false });
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(false);
        return new SendCodeCommandHandler(
            _context, smsMock.Object, IdentityPhone(),
            new Mock<ILogger<SendCodeCommandHandler>>().Object, authMock.Object, emailMock.Object);
    }

    private VerifyCodeCommandHandler BuildInnerVerify()
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
            RefreshTokenExpirationDays = 14
        });

        return new VerifyCodeCommandHandler(
            _context,
            tokenServiceMock.Object,
            IdentityPhone(),
            jwtOptionsMock.Object,
            new Mock<ILogger<VerifyCodeCommandHandler>>().Object,
            new ProviderEventService(_context));
    }

    private AdminSendCodeCommandHandler BuildAdminSendCode(Mock<ISmsService> smsMock) =>
        new(_context, IdentityPhone(), BuildInnerSendCode(smsMock),
            new Mock<ILogger<AdminSendCodeCommandHandler>>().Object);

    private AdminVerifyCodeCommandHandler BuildAdminVerify() =>
        new(_context, IdentityPhone(), BuildInnerVerify(),
            new Mock<ILogger<AdminVerifyCodeCommandHandler>>().Object);

    private async Task SeedUser(string phone, string roleName, bool banned = false, bool deleted = false)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = DateTime.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            IsActive = true,
            IsBanned = banned,
            IsDeleted = deleted,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedValidCode(string phone, string rawCode)
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = SecretsHasher.Hash(rawCode),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        });
        await _context.SaveChangesAsync();
    }

    // --- AdminSendCode ---------------------------------------------------

    [Theory]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.ManagerName)]
    public async Task AdminSendCode_StaffPhone_GeneratesAndSendsCode(string staffRole)
    {
        const string phone = "+380991112233";
        await SeedUser(phone, staffRole);
        var smsMock = new Mock<ISmsService>();

        var result = await BuildAdminSendCode(smsMock).HandleAsync(new AdminSendCodeCommand(phone), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await _context.VerificationCodes.CountAsync(v => v.PhoneNumber == phone)).Should().Be(1);
        smsMock.Verify(x => x.SendVerificationCodeAsync(phone, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminSendCode_NormalUser_SendsNothing_ButReturnsSuccess()
    {
        const string phone = "+380995556677";
        await SeedUser(phone, SeedRoles.UserName);
        var smsMock = new Mock<ISmsService>();

        var result = await BuildAdminSendCode(smsMock).HandleAsync(new AdminSendCodeCommand(phone), CancellationToken.None);

        // Non-enumeration: same success shape a staff phone would return...
        result.Success.Should().BeTrue();
        // ...but no code was created and no SMS/email was sent.
        (await _context.VerificationCodes.CountAsync(v => v.PhoneNumber == phone)).Should().Be(0);
        smsMock.Verify(x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminSendCode_UnknownPhone_SendsNothing_ButReturnsSuccess()
    {
        const string phone = "+380990000000";
        var smsMock = new Mock<ISmsService>();

        var result = await BuildAdminSendCode(smsMock).HandleAsync(new AdminSendCodeCommand(phone), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await _context.VerificationCodes.CountAsync(v => v.PhoneNumber == phone)).Should().Be(0);
        smsMock.Verify(x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminSendCode_BannedStaff_SendsNothing()
    {
        const string phone = "+380991230000";
        await SeedUser(phone, SeedRoles.AdminName, banned: true);
        var smsMock = new Mock<ISmsService>();

        var result = await BuildAdminSendCode(smsMock).HandleAsync(new AdminSendCodeCommand(phone), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await _context.VerificationCodes.CountAsync(v => v.PhoneNumber == phone)).Should().Be(0);
        smsMock.Verify(x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminSendCode_DeletedStaff_SendsNothing()
    {
        const string phone = "+380991230001";
        await SeedUser(phone, SeedRoles.AdminName, deleted: true);
        var smsMock = new Mock<ISmsService>();

        await BuildAdminSendCode(smsMock).HandleAsync(new AdminSendCodeCommand(phone), CancellationToken.None);

        (await _context.VerificationCodes.CountAsync(v => v.PhoneNumber == phone)).Should().Be(0);
        smsMock.Verify(x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- AdminVerify -----------------------------------------------------

    [Fact]
    public async Task AdminVerify_StaffPhoneWithValidCode_IssuesTokens()
    {
        const string phone = "+380991112244";
        await SeedUser(phone, SeedRoles.AdminName);
        await SeedValidCode(phone, "123456");

        var result = await BuildAdminVerify().HandleAsync(new AdminVerifyCodeCommand(phone, "123456"), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
        (await _context.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AdminVerify_NonStaffPhone_Rejected_EvenWithValidCode()
    {
        const string phone = "+380995550011";
        await SeedUser(phone, SeedRoles.UserName);
        await SeedValidCode(phone, "123456");

        var act = () => BuildAdminVerify().HandleAsync(new AdminVerifyCodeCommand(phone, "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");
        // No session was issued to a non-staff caller even though the code itself was valid.
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AdminVerify_UnknownPhone_Rejected_AndCreatesNoUser()
    {
        const string phone = "+380990000009";
        await SeedValidCode(phone, "123456");

        var act = () => BuildAdminVerify().HandleAsync(new AdminVerifyCodeCommand(phone, "123456"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _context.Users.CountAsync()).Should().Be(0);
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    // --- inner guard: verify with registration disabled ------------------

    [Fact]
    public async Task VerifyCode_WithRegistrationDisabled_UnknownPhone_ThrowsAndCreatesNoUser()
    {
        // Defense-in-depth: even reaching the shared handler directly with allowRegistration:false
        // (the admin path) must never mint an account, and must burn the code so it can't be reused.
        const string phone = "+380990000010";
        await SeedValidCode(phone, "123456");

        var act = () => BuildInnerVerify().HandleAsync(
            new VerifyCodeCommand(phone, "123456"), CancellationToken.None, allowRegistration: false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await _context.Users.CountAsync()).Should().Be(0);
        var code = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == phone);
        code.IsUsed.Should().BeTrue("a consumed admin-login code must not be replayable");
    }
}
