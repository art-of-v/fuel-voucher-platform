using FluentAssertions;
using FuelFlow.Features.Auth.QaTestAccess;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
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
/// Covers the rebuilt QA App-Store test-access mechanism: the server-side switch, the send/verify
/// paths, the admin toggle with session revocation, and the security boundaries that keep it from
/// becoming a general authentication bypass. The QA code used here is a throwaway test value — the
/// real production code is never embedded in source.
/// </summary>
public sealed class QaTestAccessTests : IDisposable
{
    // Deliberately NOT the production code. Tests only need the configured hash and the submitted
    // code to correspond.
    private const string TestQaCode = "424242";
    private static readonly string TestQaCodeHash = SecretsHasher.Hash(TestQaCode);
    private const string QaPhone = AuthOptions.QaTestAccountPhoneNumber;

    private readonly ApplicationDbContext _context;

    public QaTestAccessTests()
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

    private static IOptions<AuthOptions> AuthOpts(string? codeHash)
    {
        var mock = new Mock<IOptions<AuthOptions>>();
        mock.Setup(o => o.Value).Returns(new AuthOptions
        {
            DevBypass = false,
            AdminOtpViaEmail = false,
            QaTestAccessCodeHash = codeHash
        });
        return mock.Object;
    }

    private QaTestAccessService BuildQaService(string? codeHash)
        => new(AuthOpts(codeHash), new RuntimeSettingsService(_context), IdentityPhone(),
            new Mock<ILogger<QaTestAccessService>>().Object);

    private SendCodeCommandHandler BuildSendCode(QaTestAccessService qa, Mock<ISmsService> smsMock, string? codeHash)
    {
        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(false);
        return new SendCodeCommandHandler(
            _context, smsMock.Object, IdentityPhone(),
            new Mock<ILogger<SendCodeCommandHandler>>().Object, AuthOpts(codeHash), emailMock.Object,
            qa);
    }

    private VerifyCodeCommandHandler BuildVerify(QaTestAccessService qa)
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
            _context, tokenServiceMock.Object, IdentityPhone(), jwtOptionsMock.Object,
            new Mock<ILogger<VerifyCodeCommandHandler>>().Object, new ProviderEventService(_context),
            qaTestAccess: qa);
    }

    private SetQaTestAccessCommandHandler BuildSetQa()
        => new(_context, new RuntimeSettingsService(_context), new ProviderEventService(_context),
            new Mock<ILogger<SetQaTestAccessCommandHandler>>().Object);

    private async Task SeedQaUserAsync(bool active = true)
    {
        if (!await _context.Roles.AnyAsync(r => r.Id == SeedRoles.UserRoleId))
        {
            _context.Roles.Add(new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow });
        }
        _context.Users.Add(new User
        {
            Id = AuthOptions.QaTestAccountUserId,
            PhoneNumber = QaPhone,
            RoleId = SeedRoles.UserRoleId,
            IsActive = active,
            IsQaAccount = true,
            TokenVersion = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task SetSwitchAsync(bool enabled)
        => await new RuntimeSettingsService(_context).UpsertAsync(AppSettingKeys.QaTestAccessEnabled, enabled.ToString());

    // --- configuration / fail-safe --------------------------------------

    [Fact]
    public async Task IsActive_True_WhenConfiguredAndSwitchOn()
    {
        var qa = BuildQaService(TestQaCodeHash);
        await SetSwitchAsync(true);
        (await qa.IsActiveAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsActive_False_WhenSwitchOff()
    {
        var qa = BuildQaService(TestQaCodeHash);
        await SetSwitchAsync(false);
        (await qa.IsActiveAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsActive_False_WhenSettingMissing()
    {
        var qa = BuildQaService(TestQaCodeHash);
        // No app_settings row at all.
        (await qa.IsActiveAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsActive_False_WhenSettingMalformed()
    {
        var qa = BuildQaService(TestQaCodeHash);
        await new RuntimeSettingsService(_context).UpsertAsync(AppSettingKeys.QaTestAccessEnabled, "definitely-not-a-bool");
        (await qa.IsActiveAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task IsActive_False_WhenNotConfigured_EvenIfSwitchOn()
    {
        var qa = BuildQaService(codeHash: null); // no QA code configured
        await SetSwitchAsync(true);
        qa.IsConfigured.Should().BeFalse();
        (await qa.IsActiveAsync()).Should().BeFalse();
    }

    // --- send-code -------------------------------------------------------

    [Fact]
    public async Task SendCode_StoresQaHashAndSkipsSms_WhenEnabled()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();

        var result = await BuildSendCode(qa, smsMock, TestQaCodeHash)
            .HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);

        result.Success.Should().BeTrue();

        var stored = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == QaPhone);
        stored.Code.Should().Be(TestQaCodeHash);               // stored as a hash, never plaintext
        stored.Code.Should().NotBe(TestQaCode);
        smsMock.Verify(x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendCode_QaPhoneDisabled_BehavesLikeOrdinaryPhone()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(false);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();

        await BuildSendCode(qa, smsMock, TestQaCodeHash)
            .HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);

        // A random code was generated and SMS attempted — the QA hash must NOT have been used.
        var stored = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == QaPhone);
        stored.Code.Should().NotBe(TestQaCodeHash);
        smsMock.Verify(x => x.SendVerificationCodeAsync(QaPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendCode_ArbitraryPhone_NeverGetsQaHash_EvenWhenEnabled()
    {
        const string otherPhone = "+380991112233";
        await SeedQaUserAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();

        await BuildSendCode(qa, smsMock, TestQaCodeHash)
            .HandleAsync(new SendCodeCommand(otherPhone), CancellationToken.None);

        var stored = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == otherPhone);
        stored.Code.Should().NotBe(TestQaCodeHash);
        smsMock.Verify(x => x.SendVerificationCodeAsync(otherPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- verify ----------------------------------------------------------

    [Fact]
    public async Task Verify_Succeeds_WithValidQaCode_WhenEnabled()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);

        var response = await BuildVerify(qa).HandleAsync(new VerifyCodeCommand(QaPhone, TestQaCode), CancellationToken.None);

        response.AccessToken.Should().Be("access-token");
        var qaUser = await _context.Users.SingleAsync(u => u.PhoneNumber == QaPhone);
        qaUser.IsQaAccount.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_Fails_WithWrongQaCode_WhenEnabled()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);

        var act = async () => await BuildVerify(qa).HandleAsync(new VerifyCodeCommand(QaPhone, "000001"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Verify_Fails_WhenSwitchOff_EvenWithOutstandingQaCode()
    {
        await SeedQaUserAsync();
        // Simulate a code issued while enabled...
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);

        // ...then the admin turns it off. The verify-time gate must reject it.
        await SetSwitchAsync(false);

        var act = async () => await BuildVerify(qa).HandleAsync(new VerifyCodeCommand(QaPhone, TestQaCode), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Verify_QaCode_CannotAuthenticateArbitraryPhone()
    {
        // The QA code is only ever stored for the QA phone. An attacker who knows the QA code and
        // requests a code for their own number gets a random one, so submitting the QA code fails.
        const string attackerPhone = "+380995556677";
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(attackerPhone), CancellationToken.None);

        var act = async () => await BuildVerify(qa).HandleAsync(new VerifyCodeCommand(attackerPhone, TestQaCode), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Verify_QaCode_IsRateLimitedPerCode()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(QaPhone), CancellationToken.None);
        var verify = BuildVerify(qa);

        // Exhaust the per-code attempt budget with wrong guesses.
        for (var i = 0; i < VerifyCodeCommandHandler.MaxFailedAttempts; i++)
        {
            var bad = async () => await verify.HandleAsync(new VerifyCodeCommand(QaPhone, "000001"), CancellationToken.None);
            await bad.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        // The code is now invalidated: even the correct QA code no longer works.
        var good = async () => await verify.HandleAsync(new VerifyCodeCommand(QaPhone, TestQaCode), CancellationToken.None);
        await good.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // --- admin toggle + revocation --------------------------------------

    [Fact]
    public async Task SetQa_Enable_WritesSettingAndAudit()
    {
        var actor = Guid.NewGuid();
        var result = await BuildSetQa().HandleAsync(
            new SetQaTestAccessCommand(Enabled: true, actor, "Owner O", SeedRoles.ProductOwnerName), CancellationToken.None);

        result.Enabled.Should().BeTrue();
        (await new RuntimeSettingsService(_context).IsQaTestAccessEnabledAsync()).Should().BeTrue();

        var audit = await _context.Set<ProviderEventOutbox>().SingleAsync(e => e.AggregateType == "QaTestAccess");
        audit.EventType.Should().Be("QaTestAccessEnabled");
        audit.ChangedByUserId.Should().Be(actor);
        audit.NewValue.Should().Contain("true"); // JSON: {"enabled":true,...}
        // The QA code must never appear in the audit trail.
        audit.NewValue.Should().NotContain(TestQaCode);
        audit.Summary.Should().NotContain(TestQaCode);
    }

    [Fact]
    public async Task SetQa_Disable_RevokesQaSessionsAndBurnsCodes()
    {
        await SeedQaUserAsync();
        await SetSwitchAsync(true);

        // A live QA session + an outstanding code.
        _context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = AuthOptions.QaTestAccountUserId,
            FamilyId = Guid.NewGuid(),
            Token = SecretsHasher.Hash("live-refresh"),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14),
            CreatedAtUtc = DateTime.UtcNow,
            IsRevoked = false
        });
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = QaPhone,
            Code = TestQaCodeHash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTime.UtcNow,
            IsUsed = false
        });
        await _context.SaveChangesAsync();

        var result = await BuildSetQa().HandleAsync(
            new SetQaTestAccessCommand(Enabled: false, Guid.NewGuid(), "Admin A", SeedRoles.AdminName), CancellationToken.None);

        result.Enabled.Should().BeFalse();
        result.RevokedSessions.Should().Be(1);

        var qaUser = await _context.Users.SingleAsync(u => u.Id == AuthOptions.QaTestAccountUserId);
        qaUser.TokenVersion.Should().Be(2); // bumped, invalidates outstanding access tokens
        (await _context.RefreshTokens.SingleAsync(rt => rt.UserId == qaUser.Id)).IsRevoked.Should().BeTrue();
        (await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == QaPhone)).IsUsed.Should().BeTrue();

        var audit = await _context.Set<ProviderEventOutbox>().SingleAsync(e => e.EventType == "QaTestAccessDisabled");
        audit.OldValue.Should().Contain("true");  // was enabled
        audit.NewValue.Should().Contain("false"); // now disabled
    }

    [Fact]
    public async Task SetQa_Idempotent_NoAuditWhenUnchanged()
    {
        // Already off (no row = off). Disabling again is a no-op.
        var result = await BuildSetQa().HandleAsync(
            new SetQaTestAccessCommand(Enabled: false, Guid.NewGuid(), "Admin A", SeedRoles.AdminName), CancellationToken.None);

        result.Enabled.Should().BeFalse();
        result.RevokedSessions.Should().Be(0);
        (await _context.Set<ProviderEventOutbox>().AnyAsync(e => e.AggregateType == "QaTestAccess")).Should().BeFalse();
    }

    [Fact]
    public async Task NormalUser_Unaffected_WhenQaConfiguredAndEnabled()
    {
        // A normal customer authenticating is untouched by QA being on.
        const string normalPhone = "+380671230000";
        var role = new Role { Id = SeedRoles.UserRoleId, Name = SeedRoles.UserName, CreatedAtUtc = DateTime.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = normalPhone,
            RoleId = role.Id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await SetSwitchAsync(true);
        var qa = BuildQaService(TestQaCodeHash);
        var smsMock = new Mock<ISmsService>();

        // Normal send-code goes to SMS with a random code, unaffected by QA.
        await BuildSendCode(qa, smsMock, TestQaCodeHash).HandleAsync(new SendCodeCommand(normalPhone), CancellationToken.None);
        var stored = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == normalPhone);
        stored.Code.Should().NotBe(TestQaCodeHash);
        smsMock.Verify(x => x.SendVerificationCodeAsync(normalPhone, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // The QA code cannot verify the normal account.
        var act = async () => await BuildVerify(qa).HandleAsync(new VerifyCodeCommand(normalPhone, TestQaCode), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
