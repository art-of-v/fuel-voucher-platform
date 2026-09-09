using FluentAssertions;
using FuelFlow.Features.Support;
using FuelFlow.Features.Support.CreateSupportMessage;
using FuelFlow.Features.Support.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FuelFlow.UnitTests.Support;

public sealed class SupportMessageHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ISupportMailSender> _mailSender = new(MockBehavior.Strict);

    public SupportMessageHandlerTests()
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

    private CreateSupportMessageCommandHandler CreateHandler() =>
        new(_context, _mailSender.Object, NullLogger<CreateSupportMessageCommandHandler>.Instance);

    private void SetupSender(bool configured)
    {
        _mailSender.SetupGet(s => s.IsConfigured).Returns(configured);
        _mailSender
            .Setup(s => s.SendAsync(It.IsAny<SupportMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_PersistsMessage_BeforeDelivery()
    {
        var handler = CreateHandler();
        SetupSender(configured: true);

        var result = await handler.HandleAsync(
            new CreateSupportMessageCommand("customer@example.com", "Оплата пройшла, талона немає.", null),
            "Mozilla/5.0",
            "203.0.113.10",
            CancellationToken.None);

        var stored = await _context.SupportMessages.SingleAsync();
        stored.Id.Should().Be(result.Id);
        stored.Email.Should().Be("customer@example.com");
        stored.Message.Should().Contain("Оплата пройшла");
        stored.UserAgent.Should().Be("Mozilla/5.0");
        stored.IpAddress.Should().Be("203.0.113.10");
        stored.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task HandleAsync_MailSenderThrows_MessageIsStillStoredWithSendError()
    {
        var handler = CreateHandler();
        _mailSender.SetupGet(s => s.IsConfigured).Returns(true);
        _mailSender
            .Setup(s => s.SendAsync(It.IsAny<SupportMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP connect failed"));

        var result = await handler.HandleAsync(
            new CreateSupportMessageCommand("customer@example.com", "Test message", null),
            null,
            null,
            CancellationToken.None);

        var stored = await _context.SupportMessages.SingleAsync(m => m.Id == result.Id);
        stored.EmailSentAtUtc.Should().BeNull();
        stored.SendError.Should().Contain("SMTP connect failed");
    }

    [Fact]
    public async Task HandleAsync_SuccessfulDelivery_RecordsEmailSentAtUtc()
    {
        var handler = CreateHandler();
        SetupSender(configured: true);

        var result = await handler.HandleAsync(
            new CreateSupportMessageCommand("customer@example.com", "Test message", null),
            null,
            null,
            CancellationToken.None);

        var stored = await _context.SupportMessages.SingleAsync(m => m.Id == result.Id);
        stored.EmailSentAtUtc.Should().NotBeNull();
        stored.SendError.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnconfiguredMail_DeliveryIsNotRecordedAsSent()
    {
        // Regression: when SMTP credentials are absent, SendAsync is a silent
        // no-op. Stamping EmailSentAtUtc anyway made the row claim a delivery
        // that never happened, hiding every message from follow-up queries.
        var handler = CreateHandler();
        SetupSender(configured: false);

        var result = await handler.HandleAsync(
            new CreateSupportMessageCommand("customer@example.com", "Test message", null),
            null,
            null,
            CancellationToken.None);

        var stored = await _context.SupportMessages.SingleAsync(m => m.Id == result.Id);
        stored.EmailSentAtUtc.Should().BeNull();
        stored.SendError.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_TrimsEmailAndMessage()
    {
        var handler = CreateHandler();
        SetupSender(configured: true);

        await handler.HandleAsync(
            new CreateSupportMessageCommand("  padded@example.com  ", "  padded message  ", null),
            null,
            null,
            CancellationToken.None);

        var stored = await _context.SupportMessages.SingleAsync();
        stored.Email.Should().Be("padded@example.com");
        stored.Message.Should().Be("padded message");
    }
}

public sealed class SupportMessageValidatorTests
{
    [Theory]
    [InlineData("valid@example.com", "Hello", true)]
    [InlineData("", "Hello", false)]
    [InlineData("not-an-email", "Hello", false)]
    [InlineData("valid@example.com", "", false)]
    [InlineData(null, "Hello", false)]
    public void Validate_BasicCases(string? email, string message, bool expectedValid)
    {
        var validator = new CreateSupportMessageCommandValidator();
        var result = validator.Validate(new CreateSupportMessageCommand(email!, message, null));
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validate_MessageOver4000Chars_Fails()
    {
        var validator = new CreateSupportMessageCommandValidator();
        var result = validator.Validate(new CreateSupportMessageCommand("a@b.com", new string('x', 4001), null));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MessageAt4000Chars_Passes()
    {
        var validator = new CreateSupportMessageCommandValidator();
        var result = validator.Validate(new CreateSupportMessageCommand("a@b.com", new string('x', 4000), null));
        result.IsValid.Should().BeTrue();
    }
}
