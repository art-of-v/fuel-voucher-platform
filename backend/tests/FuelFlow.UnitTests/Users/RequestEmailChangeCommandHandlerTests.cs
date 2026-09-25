using FluentAssertions;
using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Auth.Verify;
using FuelFlow.Features.Providers;
using FuelFlow.Features.Users.ChangeEmail;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Notifications.Email;
using FuelFlow.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FuelFlow.UnitTests.Users;

/// <summary>
/// The self-service email change is now step-up-guarded: a fresh OTP (delivered to the current
/// phone/email, never the new address) is required before the new address is staged, and the active
/// email only changes when the emailed confirmation link is opened. These tests pin that contract:
/// a valid code stages + emails the new address; a bad/absent code is rejected and stages nothing;
/// and pre-OTP validation never burns the user's code.
/// </summary>
public sealed class RequestEmailChangeCommandHandlerTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private const string Phone = "+380991112233";
    private const string CurrentEmail = "old@example.com";
    private const string NewEmail = "new@example.com";
    private const string ValidCode = "654321";
    private const string ConfirmBase = "https://api.test";

    private readonly ApplicationDbContext _context;
    private readonly List<(string To, EmailMessage Message)> _sends = new();

    public RequestEmailChangeCommandHandlerTests()
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

    private async Task SeedUserAsync(string? email = CurrentEmail)
    {
        _context.Users.Add(new User
        {
            Id = UserId,
            PhoneNumber = Phone,
            Email = email,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task<VerificationCode> SeedCodeAsync(string code = ValidCode, string phone = Phone)
    {
        var vc = new VerificationCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            Code = SecretsHasher.Hash(code),
            IsUsed = false,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5),
            CreatedAtUtc = DateTime.UtcNow
        };
        _context.VerificationCodes.Add(vc);
        await _context.SaveChangesAsync();
        return vc;
    }

    private RequestEmailChangeCommandHandler CreateHandler(bool emailConfigured = true)
    {
        var email = new Mock<IEmailSender>();
        email.SetupGet(e => e.IsConfigured).Returns(emailConfigured);
        email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((to, message, _) => _sends.Add((to, message)))
            .Returns(Task.CompletedTask);

        return new RequestEmailChangeCommandHandler(
            _context,
            new OtpVerificationService(_context),
            email.Object,
            new ProviderEventService(_context),
            NullLogger<RequestEmailChangeCommandHandler>.Instance);
    }

    private static RequestEmailChangeCommand Command(string newEmail = NewEmail, string code = ValidCode) =>
        new(UserId.ToString(), newEmail, code, ConfirmBase);

    [Fact]
    public async Task Request_WithValidCode_StagesPendingChangeAndEmailsNewAddress()
    {
        await SeedUserAsync();
        var seededCode = await SeedCodeAsync();

        var result = await CreateHandler().HandleAsync(Command());

        result.EmailChangePending.Should().BeTrue();

        var user = await _context.Users.FindAsync(UserId);
        user!.Email.Should().Be(CurrentEmail, "the active email must not change until the link is opened");
        user.PendingEmail.Should().Be(NewEmail);
        user.PendingEmailTokenHash.Should().NotBeNullOrEmpty();
        user.PendingEmailExpiresAtUtc.Should().NotBeNull();

        // Confirmation goes to the NEW address only; the step-up code was burned.
        _sends.Should().ContainSingle();
        _sends[0].To.Should().Be(NewEmail);
        (await _context.VerificationCodes.FindAsync(seededCode.Id))!.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Request_WithWrongCode_Throws_AndStagesNothing()
    {
        await SeedUserAsync();
        await SeedCodeAsync();

        var act = () => CreateHandler().HandleAsync(Command(code: "000000"));

        await act.Should().ThrowAsync<StepUpChallengeFailedException>();
        (await _context.Users.FindAsync(UserId))!.PendingEmail.Should().BeNull();
        _sends.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WithNoCodeIssued_Throws()
    {
        await SeedUserAsync();

        var act = () => CreateHandler().HandleAsync(Command());

        await act.Should().ThrowAsync<StepUpChallengeFailedException>();
        _sends.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WithSameEmail_ThrowsArgument_AndDoesNotConsumeCode()
    {
        await SeedUserAsync();
        var seededCode = await SeedCodeAsync();

        var act = () => CreateHandler().HandleAsync(Command(newEmail: CurrentEmail));

        await act.Should().ThrowAsync<ArgumentException>();
        // Validation that needs no code must not burn the user's code.
        (await _context.VerificationCodes.FindAsync(seededCode.Id))!.IsUsed.Should().BeFalse();
        _sends.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WithInvalidEmail_ThrowsArgument()
    {
        await SeedUserAsync();
        await SeedCodeAsync();

        var act = () => CreateHandler().HandleAsync(Command(newEmail: "not-an-email"));

        await act.Should().ThrowAsync<ArgumentException>();
        _sends.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WhenEmailDeliveryNotConfigured_Throws_AndDoesNotConsumeCode()
    {
        await SeedUserAsync();
        var seededCode = await SeedCodeAsync();

        var act = () => CreateHandler(emailConfigured: false).HandleAsync(Command());

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _context.Users.FindAsync(UserId))!.PendingEmail.Should().BeNull();
        (await _context.VerificationCodes.FindAsync(seededCode.Id))!.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task Request_WithUnknownUser_Throws()
    {
        // No user seeded.
        await SeedCodeAsync();

        var act = () => CreateHandler().HandleAsync(Command());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
