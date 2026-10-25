using FluentAssertions;
using FuelFlow.Features.Auth.Security;
using FuelFlow.Features.Auth.SendCode;
using FuelFlow.Features.Auth.SendCode.Abstractions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Notifications.Push;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Abstractions;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.Auth;

/// <summary>
/// The push channel for the sign-in code: the SMS brand balance is finite, and when it runs out
/// the budget guard refuses every send, so a worker cannot sign in at all. A push costs nothing.
/// <para>
/// These tests pin the part that matters: push is a FALLBACK, not a replacement. An account with
/// no token, a stale token, a rejected push, or a deployment that never turned the channel on
/// still gets its code by SMS - that is what keeps a device from being locked out.
/// </para>
/// </summary>
public sealed class OtpDeliveryViaPushTests : IDisposable
{
    private const string Phone = "+380991234567";
    private const string DeviceToken = "ExponentPushToken[test-device]";
    private const string DeadDeviceToken = "ExponentPushToken[test-dead-device]";

    private readonly ApplicationDbContext _context;

    public OtpDeliveryViaPushTests()
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

    [Fact]
    public async Task SendCode_WithThePushChannelOn_DeliversTheCodeToTheAccountsDevices()
    {
        await SeedUserAsync(Phone, tokenActive: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();
        ExpoPushMessage? sent = null;
        CapturePush(push, message => sent = message);

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        var response = await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        response.Success.Should().BeTrue();

        sent.Should().NotBeNull();
        sent!.Token.Should().Be(DeviceToken);
        sent.Data.Should().NotBeNull();
        sent.Data!["type"].Should().Be("auth_otp");
        sent.Data["phone"].Should().Be(Phone);

        var deliveredCode = sent.Data["code"].Should().BeOfType<string>().Subject;
        deliveredCode.Should().MatchRegex("^[0-9]{6}$");

        var stored = await _context.VerificationCodes.SingleAsync(v => v.PhoneNumber == Phone);
        OtpCodeHasher.Verify(stored.Code, deliveredCode, AuthOptions.DevelopmentOtpPepper).Should().BeTrue();

        sms.Verify(
            x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOn_AndNoDeviceToken_FallsBackToSms()
    {
        await SeedUserAsync(Phone, tokenActive: false);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(Phone, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        push.Verify(
            p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOn_AndAStaleDeviceToken_FallsBackToSms()
    {
        var user = await SeedUserAsync(Phone, tokenActive: false);
        await SeedTokenAsync(user.Id, DeviceToken, active: false);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(Phone, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        push.Verify(
            p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOn_AndExpoRejectsThePush_FallsBackToSms()
    {
        await SeedUserAsync(Phone, tokenActive: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();
        push
            .Setup(p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExpoPushMessage> messages, CancellationToken _) =>
                messages
                    .Select(m => new ExpoPushResult(m.Token, ExpoPushStatus.Error, "DeviceNotRegistered"))
                    .ToList());

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(Phone, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOn_DeliversTheCodeToASignedOutDevice()
    {
        // The device signed out: its token is parked (it stopped receiving the business
        // notifications), not dead. The sign-in code still has to reach it - that is the whole
        // point of the push channel, and the SMS budget it exists to spare is gone.
        var user = await SeedUserAsync(Phone, tokenActive: false);
        await SeedTokenAsync(user.Id, DeviceToken, active: true, authOtpOnly: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();
        ExpoPushMessage? sent = null;
        CapturePush(push, message => sent = message);

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Token.Should().Be(DeviceToken);
        sms.Verify(
            x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOn_AndOneDeadDevice_KeepsTheCodeOnPush()
    {
        // One phone is gone (Expo says DeviceNotRegistered), the other took the code. The old
        // rule demanded EVERY device to accept, so one dead phone pushed the whole sign-in back
        // onto the paid SMS channel - with a budget that was already spent.
        var user = await SeedUserAsync(Phone, tokenActive: true);
        await SeedTokenAsync(user.Id, DeadDeviceToken, active: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();
        push
            .Setup(p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExpoPushMessage> messages, CancellationToken _) =>
                messages
                    .Select(m => m.Token == DeadDeviceToken
                        ? new ExpoPushResult(m.Token, ExpoPushStatus.Error, "DeviceNotRegistered")
                        : new ExpoPushResult(m.Token, ExpoPushStatus.Ok, null))
                    .ToList());

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The dead phone is not asked again next time.
        var dead = await _context.PushTokens.AsNoTracking().SingleAsync(t => t.Token == DeadDeviceToken);
        dead.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SendCode_WithThePushChannelOff_UsesSms_EvenWhenTheAccountHasADevice()
    {
        await SeedUserAsync(Phone, tokenActive: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();

        var handler = BuildHandler(sms, push, pushChannelOn: false);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(Phone, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        push.Verify(
            p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_ForAPhoneNobodyOwns_UsesSms_AndNeverPushes()
    {
        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();

        var handler = BuildHandler(sms, push, pushChannelOn: true);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        sms.Verify(
            x => x.SendVerificationCodeAsync(Phone, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        push.Verify(
            p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendCode_WithPushAndTheBreakGlassBothAvailable_ThePushWins()
    {
        // Push is free and device-bound; the acknowledged mailbox is neither. The channel order
        // is a decision - push first, the break-glass stays for accounts with no device - and
        // this pins it.
        var staff = await SeedStaffAsync(Phone, "admin@palne.shop", withSecondFactor: true);
        await SeedTokenAsync(staff.Id, DeviceToken, active: true);

        var sms = new Mock<ISmsService>();
        var push = new Mock<IExpoPushSender>();
        CapturePush(push, _ => { });
        var email = new Mock<IEmailSender>();
        email.SetupGet(e => e.IsConfigured).Returns(true);

        var handler = BuildHandler(
            sms, push, pushChannelOn: true,
            adminOtpViaEmail: true, breakGlassAcknowledged: true, email: email);

        await handler.HandleAsync(new SendCodeCommand(Phone), CancellationToken.None);

        push.Verify(
            p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        email.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sms.Verify(
            x => x.SendVerificationCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void CapturePush(Mock<IExpoPushSender> push, Action<ExpoPushMessage> capture)
    {
        push
            .Setup(p => p.SendAsync(It.IsAny<IReadOnlyList<ExpoPushMessage>>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyList<ExpoPushMessage> messages, CancellationToken _) =>
            {
                foreach (var message in messages)
                    capture(message);
            })
            .ReturnsAsync((IReadOnlyList<ExpoPushMessage> messages, CancellationToken _) =>
                messages
                    .Select(m => new ExpoPushResult(m.Token, ExpoPushStatus.Ok, null))
                    .ToList());
    }

    private SendCodeCommandHandler BuildHandler(
        Mock<ISmsService> sms,
        Mock<IExpoPushSender> push,
        bool pushChannelOn,
        bool adminOtpViaEmail = false,
        bool breakGlassAcknowledged = false,
        Mock<IEmailSender>? email = null)
    {
        var phoneMock = new Mock<IPhoneNumberService>();
        phoneMock.Setup(x => x.Normalize(It.IsAny<string>())).Returns((string phone) => phone);

        var authMock = new Mock<IOptions<AuthOptions>>();
        authMock.Setup(o => o.Value).Returns(new AuthOptions
        {
            DevBypass = false,
            AdminOtpViaEmail = adminOtpViaEmail,
            AdminOtpViaEmailBreakGlassAcknowledged = breakGlassAcknowledged,
            OtpDeliveryViaPush = pushChannelOn
        });

        email ??= new Mock<IEmailSender>();
        email.SetupGet(e => e.IsConfigured).Returns(false);

        return new SendCodeCommandHandler(
            _context,
            sms.Object,
            phoneMock.Object,
            new Mock<ILogger<SendCodeCommandHandler>>().Object,
            authMock.Object,
            email.Object,
            new AuditEventService(_context),
            qaTestAccess: null,
            metrics: null,
            pushSender: push.Object);
    }

    /// <summary>A staff account with an email on file, the way TryResolveStaffAsync wants it.</summary>
    private async Task<User> SeedStaffAsync(string phone, string email, bool withSecondFactor)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = AuthOptions.AdminRoleName, CreatedAtUtc = TestClock.UtcNow };
        _context.Roles.Add(role);
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Email = email,
            RoleId = role.Id,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Users.Add(user);

        if (withSecondFactor)
        {
            _context.StaffTwoFactors.Add(new StaffTwoFactor
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SecretCiphertext = TestQrCipher.Instance.Encrypt("JBSWY3DPEHPK3PXP"),
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<User> SeedUserAsync(string phone, bool tokenActive)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            IsActive = true,
            IsDeleted = false,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        };
        _context.Users.Add(user);

        if (tokenActive)
            await SeedTokenAsync(user.Id, DeviceToken, active: true);

        await _context.SaveChangesAsync();
        return user;
    }

    private async Task SeedTokenAsync(Guid userId, string token, bool active, bool authOtpOnly = false)
    {
        _context.PushTokens.Add(new UserPushToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            Platform = "ios",
            IsActive = active,
            AuthOtpOnly = authOtpOnly,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            LastSeenAtUtc = TestClock.UtcNow
        });
        await _context.SaveChangesAsync();
    }
}
