using FluentAssertions;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Providers;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// Verification-code lifecycle rules that were exercised live against production but had no
/// automated coverage: the newest-code-wins selection in VerifyCodeCommandHandler, the
/// supersession sweep in SendCodeCommandHandler, and the stored-hash format contract. The
/// expired-code, used-code, wrong-attempt-count and max-attempt-burn rules already live in
/// <see cref="AuthHandlersExtraTests"/>; these fill the gaps around them.
/// </summary>
public sealed class CodeLifecycleTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public CodeLifecycleTests()
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

    // --- Newest-code-wins at verify -------------------------------------------------------

    [Fact]
    public async Task VerifyCode_ShouldRejectOlderCode_WhenANewerUnusedCodeExists()
    {
        // Two coexisting unused, unexpired codes for one phone. The handler orders by
        // CreatedAtUtc DESC and only ever loads the newest, so the older code must not verify
        // even though its own row is still unused and unexpired - the "no second active code"
        // guarantee, enforced defensively at verify time on top of the send-time sweep below.
        const string phone = "+380991110001";
        SeedCode(phone, "111111", createdMinutesAgo: 2);
        SeedCode(phone, "222222", createdMinutesAgo: 0);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyHandler();

        var act = () => handler.HandleAsync(new VerifyCodeCommand(phone, "111111"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();

        // The failed attempt landed on the NEWEST row; the older row is untouched.
        var newest = await GetCode(phone, "222222");
        var older = await GetCode(phone, "111111");
        newest.FailedAttempts.Should().Be(1);
        older.FailedAttempts.Should().Be(0);
        older.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyCode_ShouldAcceptNewestCode_WhenAnOlderUnusedCodeExists()
    {
        const string phone = "+380991110002";
        SeedCode(phone, "111111", createdMinutesAgo: 2);
        SeedCode(phone, "222222", createdMinutesAgo: 0);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyHandler();

        var response = await handler.HandleAsync(new VerifyCodeCommand(phone, "222222"), CancellationToken.None);

        response.AccessToken.Should().NotBeNullOrEmpty();
        var newest = await GetCode(phone, "222222");
        newest.IsUsed.Should().BeTrue();
        newest.UsedAtUtc.Should().NotBeNull();
    }

    // --- Supersession at send-time --------------------------------------------------------

    [Fact]
    public async Task SendCode_ShouldSupersedeExistingUnusedCode_LeavingOnlyTheNewestActive()
    {
        // Sending a second code must invalidate the first so that only one code is ever live
        // for a phone (spec: no second active code). SendCodeCommandHandler marks every unused
        // row used before inserting the new one.
        const string phone = "+380991110003";

        var handler = BuildSendHandler();
        await handler.HandleAsync(new SendCodeCommand(phone), CancellationToken.None);
        await handler.HandleAsync(new SendCodeCommand(phone), CancellationToken.None);

        var rows = await _context.VerificationCodes
            .Where(v => v.PhoneNumber == phone)
            .OrderBy(v => v.CreatedAtUtc)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows.Count(v => !v.IsUsed).Should().Be(1, "only the newest code stays active");
        rows[0].IsUsed.Should().BeTrue("the first code is superseded by the second");
        rows[^1].IsUsed.Should().BeFalse();
    }

    // --- Stored-hash format contract ------------------------------------------------------

    [Fact]
    public void SecretsHasher_ShouldProduceUppercaseHexSha256()
    {
        // Codes are stored as SHA-256 in uppercase hex. Pinned here at the source so a change
        // to the hashing scheme is caught directly, not only through a handler round-trip.
        // "111111" -> known SHA-256 (uppercase hex).
        SecretsHasher.Hash("111111").Should()
            .Be("BCB15F821479B4D5772BD0CA866C00AD5F926E3580720659CC80D39C9D09802A");
        SecretsHasher.Hash("111111").Should().MatchRegex("^[0-9A-F]{64}$");
        SecretsHasher.Hash("abc").Should().Be(SecretsHasher.Hash("abc"), "hashing is deterministic");
    }

    // --- Staff-login audit ----------------------------------------------------------------

    [Fact]
    public async Task VerifyCode_ShouldAuditStaffLogin_ForProductOwner()
    {
        // The audit gate used to fire only for the literal role name "Admin", so a
        // ProductOwner (or Manager) login produced no audit row - verified live against
        // prod. It now fires for any staff role via SeedRoles.IsStaff, carrying the actual
        // role. A successful PO login must leave a StaffLoggedIn record naming ProductOwner.
        const string phone = "+380991110009";
        var role = new Role { Id = SeedRoles.ProductOwnerId, Name = SeedRoles.ProductOwnerName, CreatedAtUtc = DateTime.UtcNow };
        _context.Roles.Add(role);
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            RoleId = role.Id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        SeedCode(phone, "111111", createdMinutesAgo: 0);
        await _context.SaveChangesAsync();

        var handler = BuildVerifyHandler();
        await handler.HandleAsync(new VerifyCodeCommand(phone, "111111"), CancellationToken.None);

        var audit = await _context.Set<ProviderEventOutbox>()
            .SingleAsync(e => e.EventType == "StaffLoggedIn");
        audit.AggregateType.Should().Be("Auth");
        audit.NewValue.Should().Contain(SeedRoles.ProductOwnerName, "the audit payload must name the actual staff role");
    }

    // --- Helpers --------------------------------------------------------------------------

    private void SeedCode(string phone, string plainCode, int createdMinutesAgo)
    {
        _context.VerificationCodes.Add(new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = SecretsHasher.Hash(plainCode),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-createdMinutesAgo),
            IsUsed = false,
            FailedAttempts = 0
        });
    }

    private async Task<VerificationCode> GetCode(string phone, string plainCode)
    {
        var hash = SecretsHasher.Hash(plainCode);
        return await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == phone && v.Code == hash);
    }

    private VerifyCodeCommandHandler BuildVerifyHandler()
    {
        var tokenServiceMock = new Mock<IJwtTokenService>();
        tokenServiceMock
            .Setup(x => x.GenerateAccessToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .Returns("access-token");
        tokenServiceMock.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var phoneNumberServiceMock = new Mock<IPhoneNumberService>();
        phoneNumberServiceMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

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

    private SendCodeCommandHandler BuildSendHandler()
    {
        var phoneMock = new Mock<IPhoneNumberService>();
        phoneMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authMock = new Mock<IOptions<AuthOptions>>();
        authMock.Setup(o => o.Value).Returns(new AuthOptions { DevBypass = false, AdminOtpViaEmail = false });

        var emailMock = new Mock<IEmailSender>();
        emailMock.SetupGet(e => e.IsConfigured).Returns(false);

        return new SendCodeCommandHandler(
            _context,
            new Mock<ISmsService>().Object,
            phoneMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authMock.Object,
            emailMock.Object);
    }
}
