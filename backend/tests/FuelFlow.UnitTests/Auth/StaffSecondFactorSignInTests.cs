using FluentAssertions;
using FuelFlow.Features.Auth.AdminLogin;
using FuelFlow.Features.Auth.Security;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.TwoFactor;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Brands;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// A staff sign-in cannot complete with a single factor.
/// </summary>
/// <remarks>
/// <para>
/// This is the acceptance criterion of planning #621 (S-12): the staff tier can move money, and
/// the only factor it used to have was a code that arrives in a mailbox. The tests below pin the
/// property for every role in <see cref="SeedRoles.StaffRoleNames"/> - not for "Admin" alone,
/// because Manager moves money as well - and on every path that can mint a staff session, not
/// only the admin one.
/// </para>
/// <para>
/// The shared <c>/verify</c> endpoint is in the list on purpose: it resolves the same account and
/// would otherwise hand a staff token to whoever proved possession of the mailbox, making the
/// admin panel's second factor a decoration.
/// </para>
/// </remarks>
public sealed class StaffSecondFactorSignInTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public StaffSecondFactorSignInTests()
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

    // --- helpers ---------------------------------------------------------

    private static IPhoneNumberService IdentityPhone()
    {
        var mock = new Mock<IPhoneNumberService>();
        mock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string p) => p);
        return mock.Object;
    }

    private StaffTwoFactorService BuildTwoFactorService() =>
        new(_context,
            TestQrCipher.Instance,
            Options.Create(new AuthOptions()),
            new Mock<ILogger<StaffTwoFactorService>>().Object);

    private VerifyCodeCommandHandler BuildVerify(AuthOptions? authOptions = null)
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

        var authMock = new Mock<IOptions<AuthOptions>>();
        authMock.Setup(o => o.Value).Returns(authOptions ?? new AuthOptions { DevBypass = false });

        return new VerifyCodeCommandHandler(
            _context,
            tokenServiceMock.Object,
            IdentityPhone(),
            jwtOptionsMock.Object,
            authMock.Object,
            BuildTwoFactorService(),
            new Mock<ILogger<VerifyCodeCommandHandler>>().Object,
            new AuditEventService(_context));
    }

    private AdminVerifyCodeCommandHandler BuildAdminVerify(AuthOptions? authOptions = null) =>
        new(_context, IdentityPhone(), BuildVerify(authOptions),
            new Mock<ILogger<AdminVerifyCodeCommandHandler>>().Object);

    private VerifyTwoFactorCommandHandler BuildTwoFactorVerify(AuthOptions? authOptions = null) =>
        new(_context, BuildTwoFactorService(), BuildVerify(authOptions),
            new Mock<ILogger<VerifyTwoFactorCommandHandler>>().Object);

    private async Task<User> SeedUserAsync(string roleName)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = TestClock.UtcNow };
        _context.Roles.Add(role);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+38099111" + TestClock.UniqueDigits(4),
            RoleId = role.Id,
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task SeedValidCodeAsync(string phone, string rawCode)
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = OtpCodeHasher.Hash(rawCode, AuthOptions.DevelopmentOtpPepper),
            ExpiresAtUtc = TestClock.UtcNow.AddMinutes(5),
            CreatedAtUtc = TestClock.UtcNow,
            IsUsed = false
        });
        await _context.SaveChangesAsync();
    }

    // --- the criterion ---------------------------------------------------

    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.ManagerName)]
    public async Task TheAdminSignIn_WithOnlyTheOtp_IssuesNoSession(string roleName)
    {
        var user = await SeedUserAsync(roleName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var result = await BuildAdminVerify().HandleAsync(
            new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        result.AccessToken.Should().BeNull("a staff sign-in does not complete with the first factor alone");
        result.RefreshToken.Should().BeNull();
        result.TwoFactor.Should().NotBeNull();
        result.TwoFactor!.Status.Should().Be("enroll", "the account has no factor yet");
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(SeedRoles.ProductOwnerName)]
    [InlineData(SeedRoles.AdminName)]
    [InlineData(SeedRoles.ManagerName)]
    public async Task TheSharedSignIn_WithOnlyTheOtp_IssuesNoSessionEither(string roleName)
    {
        // The mobile endpoint resolves the same account. If it minted a staff token, the admin
        // panel's second factor would be a decoration with a door next to it.
        var user = await SeedUserAsync(roleName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var result = await BuildVerify().HandleAsync(
            new VerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
        result.TwoFactor.Should().NotBeNull();
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ACustomerSignIn_IsUnaffected()
    {
        var user = await SeedUserAsync(SeedRoles.UserName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var result = await BuildVerify().HandleAsync(
            new VerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
        result.TwoFactor.Should().BeNull("a customer has no second factor to prove");
        (await _context.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheSecondStep_CompletesTheSignIn()
    {
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var first = await BuildAdminVerify().HandleAsync(
            new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        var secret = first.TwoFactor!.EnrollmentSecret!;
        var code = Totp.ComputeCode(secret, TestClock.UtcNow);

        var second = await BuildTwoFactorVerify().HandleAsync(
            new VerifyTwoFactorCommand(first.TwoFactor.Challenge, code), CancellationToken.None);

        second.AccessToken.Should().Be("access-token");
        second.RefreshToken.Should().Be("refresh-token");
        (await _context.RefreshTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheSecondStep_WithAWrongCode_IssuesNoSession()
    {
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var first = await BuildAdminVerify().HandleAsync(
            new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        var act = () => BuildTwoFactorVerify().HandleAsync(
            new VerifyTwoFactorCommand(first.TwoFactor!.Challenge, "000000"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or expired verification code");
        (await _context.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheOtp_IsSpentByTheFirstStep_SoItCannotBeReplayed()
    {
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        await BuildAdminVerify().HandleAsync(
            new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        var code = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == user.PhoneNumber);
        code.IsUsed.Should().BeTrue("a challenge is not a licence to try the same code again");
    }

    [Fact]
    public async Task TheFirstStep_IsRecorded_InTheAuditTrail()
    {
        // A first factor that was proven for an account that can move money, without a login
        // behind it, is a fact the audit trail has to carry.
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        await BuildAdminVerify().HandleAsync(
            new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        var events = await _context.Set<AuditEventOutbox>()
            .Where(e => e.AggregateId == user.Id.ToString())
            .ToListAsync();

        events.Should().Contain(e => e.EventType == "StaffSecondFactorPending");
        events.Should().NotContain(e => e.EventType == "StaffLoggedIn");
    }

    // --- the switches ----------------------------------------------------

    [Fact]
    public async Task TheSecondFactor_CanBeTurnedOff_ForAnEnvironmentThatIsNotProduction()
    {
        // The switch exists for the local development loop and for a rollback. Production
        // refuses to start with it off - see SecurityConfigurationValidator.
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var result = await BuildAdminVerify(new AuthOptions { DevBypass = false, StaffSecondFactorRequired = false })
            .HandleAsync(new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        result.TwoFactor.Should().BeNull();
    }

    [Fact]
    public async Task DevBypass_TurnsTheSecondFactorOff_TogetherWithEverythingElse()
    {
        var user = await SeedUserAsync(SeedRoles.AdminName);
        await SeedValidCodeAsync(user.PhoneNumber, "123456");

        var result = await BuildAdminVerify(new AuthOptions { DevBypass = true })
            .HandleAsync(new AdminVerifyCodeCommand(user.PhoneNumber, "123456"), CancellationToken.None);

        result.AccessToken.Should().Be("access-token");
        result.TwoFactor.Should().BeNull();
    }
}
