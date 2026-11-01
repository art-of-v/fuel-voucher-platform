using FluentAssertions;
using FuelFlow.Features.Auth.Refresh;
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
/// A staff panel session has a hard stop that refreshing cannot move.
/// </summary>
/// <remarks>
/// The sliding window (14 days) answers "how long does an idle session survive"; it does not
/// answer "how long does a session survive at all". A panel left open, or a cookie kept alive by a
/// script, used to stay a live staff session for as long as it was touched. The ceiling is set
/// when the session is born, copied forward on every rotation, and enforced before a refresh is
/// allowed to mint anything.
/// </remarks>
public sealed class StaffSessionLifetimeTests : IDisposable
{
    private const int CeilingHours = 12;

    private readonly ApplicationDbContext _context;

    public StaffSessionLifetimeTests()
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

    private static IJwtTokenService TokenService()
    {
        var mock = new Mock<IJwtTokenService>();
        mock.Setup(x => x.GenerateAccessToken(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        mock.Setup(x => x.GenerateRefreshToken()).Returns("rotated-refresh-token");
        return mock.Object;
    }

    private static JwtOptions JwtConfig() => new()
    {
        Secret = "secret",
        Issuer = "issuer",
        Audience = "audience",
        AccessTokenExpirationMinutes = 15,
        RefreshTokenExpirationDays = 14,
        StaffSessionLifetimeHours = CeilingHours
    };

    private VerifyCodeCommandHandler BuildIssueHandler() =>
        new(_context,
            TokenService(),
            IdentityPhone(),
            Options.Create(JwtConfig()),
            Options.Create(new AuthOptions { DevBypass = false }),
            new StaffTwoFactorService(
                _context,
                TestQrCipher.Instance,
                Options.Create(new AuthOptions()),
                new Mock<ILogger<StaffTwoFactorService>>().Object),
            new Mock<ILogger<VerifyCodeCommandHandler>>().Object,
            new AuditEventService(_context));

    private RefreshTokenCommandHandler BuildRefreshHandler() =>
        new(_context,
            TokenService(),
            Options.Create(JwtConfig()),
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

    private async Task<User> SeedUserWithRoleAsync(string roleName)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = roleName, CreatedAtUtc = TestClock.UtcNow };
        _context.Roles.Add(role);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = "+38099111" + TestClock.UniqueDigits(4),
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<RefreshToken> SeedRefreshTokenAsync(User user, DateTime? absoluteExpiresAtUtc, string raw = "refresh-token")
    {
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            RoleNameAtIssue = user.Role?.Name,
            Purpose = RefreshTokenPurpose.Session,
            Token = SecretsHasher.Hash(raw),
            ExpiresAtUtc = TestClock.UtcNow.AddDays(14),
            AbsoluteExpiresAtUtc = absoluteExpiresAtUtc,
            CreatedAtUtc = TestClock.UtcNow,
            IsRevoked = false,
            User = user
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();
        return token;
    }

    // --- issuance --------------------------------------------------------

    [Fact]
    public async Task IssueSession_GivesAStaffPanelSessionAnAbsoluteCeiling()
    {
        var user = await SeedUserWithRoleAsync(SeedRoles.AdminName);

        await BuildIssueHandler().IssueSessionAsync(user, isNewUser: false, CancellationToken.None);

        var issued = await _context.RefreshTokens.SingleAsync();
        issued.AbsoluteExpiresAtUtc.Should().NotBeNull();
        issued.AbsoluteExpiresAtUtc!.Value.Should().BeCloseTo(
            TestClock.UtcNow.AddHours(CeilingHours), TimeSpan.FromMinutes(1));
        // The sliding window is still the longer of the two; the ceiling is what bounds it.
        issued.ExpiresAtUtc.Should().BeAfter(issued.AbsoluteExpiresAtUtc!.Value);
    }

    [Fact]
    public async Task IssueSession_LeavesACustomerSessionUncapped()
    {
        var user = await SeedUserWithRoleAsync(SeedRoles.UserName);

        await BuildIssueHandler().IssueSessionAsync(user, isNewUser: false, CancellationToken.None);

        var issued = await _context.RefreshTokens.SingleAsync();
        issued.AbsoluteExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task IssueSession_LeavesANativeStaffSessionUncapped()
    {
        // The mobile app's throwaway that precedes the device-bound session: that session has a
        // device lifetime of its own, and a staff account using the phone is a customer there.
        var user = await SeedUserWithRoleAsync(SeedRoles.AdminName);

        await BuildIssueHandler().IssueSessionAsync(
            user, isNewUser: false, CancellationToken.None, deviceBindingPending: true);

        var issued = await _context.RefreshTokens.SingleAsync();
        issued.Purpose.Should().Be(RefreshTokenPurpose.DeviceBinding);
        issued.AbsoluteExpiresAtUtc.Should().BeNull();
    }

    // --- enforcement -----------------------------------------------------

    [Fact]
    public async Task Refresh_RefusesAndRevokesTheSession_OnceTheCeilingHasPassed()
    {
        var user = await SeedUserWithRoleAsync(SeedRoles.AdminName);
        var token = await SeedRefreshTokenAsync(
            user, TestClock.UtcNow.AddMinutes(-1), raw: "expired-panel-refresh");

        var act = () => BuildRefreshHandler()
            .HandleAsync(new RefreshTokenCommand("expired-panel-refresh"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        var stored = await _context.RefreshTokens.SingleAsync(rt => rt.Id == token.Id);
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedAtUtc.Should().NotBeNull();
        _context.RefreshTokens.Should().NotContain(rt => rt.Token == SecretsHasher.Hash("rotated-refresh-token"));
    }

    [Fact]
    public async Task Refresh_CarriesTheCeilingForward_OnRotation()
    {
        var user = await SeedUserWithRoleAsync(SeedRoles.AdminName);
        var ceiling = TestClock.UtcNow.AddHours(3);
        await SeedRefreshTokenAsync(user, ceiling, raw: "live-panel-refresh");

        await BuildRefreshHandler()
            .HandleAsync(new RefreshTokenCommand("live-panel-refresh"), CancellationToken.None);

        var rotated = await _context.RefreshTokens
            .SingleAsync(rt => rt.Token == SecretsHasher.Hash("rotated-refresh-token"));
        rotated.AbsoluteExpiresAtUtc.Should().Be(ceiling);
    }
}
