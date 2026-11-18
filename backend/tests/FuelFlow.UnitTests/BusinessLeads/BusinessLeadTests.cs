using FluentAssertions;
using FuelFlow.Features.BusinessLeads;
using FuelFlow.Features.BusinessLeads.CreateBusinessLead;
using FuelFlow.Features.BusinessLeads.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FuelFlow.UnitTests.BusinessLeads;

public sealed class BusinessLeadTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IBusinessLeadMailSender> _mail = new(MockBehavior.Strict);

    public BusinessLeadTests()
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Theory]
    [InlineData("+380970011771", true)]
    [InlineData("380970011771", false)]
    [InlineData("+0123456789", false)]
    [InlineData("+1234567", false)]
    [InlineData("+1234567890123456", false)]
    public void Validator_requires_explicit_E164(string phone, bool valid)
    {
        var errors = CreateBusinessLeadCommandValidator.Validate(Command(phone: phone));
        errors.ContainsKey("Phone").Should().Be(!valid);
    }

    [Fact]
    public void Validator_checks_optional_fields_only_when_supplied()
    {
        CreateBusinessLeadCommandValidator.Validate(Command(email: null, fleetSize: null))
            .Should().BeEmpty();

        var errors = CreateBusinessLeadCommandValidator.Validate(
            Command(email: "broken", fleetSize: 0, message: new string('x', 2001)));
        errors.Keys.Should().Contain(["Email", "FleetSize", "Message"]);
    }

    [Fact]
    public async Task Handler_persists_trimmed_lead_before_successful_mail()
    {
        _mail.SetupGet(m => m.IsConfigured).Returns(true);
        _mail.Setup(m => m.SendAsync(It.IsAny<BusinessLead>(), It.IsAny<CancellationToken>()))
            .Callback<BusinessLead, CancellationToken>((lead, _) =>
                _context.BusinessLeads.Any(l => l.Id == lead.Id).Should().BeTrue())
            .Returns(Task.CompletedTask);

        var result = await Handler().HandleAsync(
            Command(company: "  Fleet Ltd  ", contact: "  Iryna  ", email: "  iryna@example.com  "),
            "Mozilla",
            "203.0.113.10",
            default);

        var stored = await _context.BusinessLeads.SingleAsync(l => l.Id == result.Id);
        stored.CompanyName.Should().Be("Fleet Ltd");
        stored.ContactName.Should().Be("Iryna");
        stored.Email.Should().Be("iryna@example.com");
        stored.Status.Should().Be(BusinessLeadStatus.New);
        stored.EmailSentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Handler_keeps_lead_when_mail_fails_without_logging_PII()
    {
        _mail.SetupGet(m => m.IsConfigured).Returns(true);
        _mail.Setup(m => m.SendAsync(It.IsAny<BusinessLead>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP down"));
        var logger = new CapturingLogger<CreateBusinessLeadCommandHandler>();

        var result = await Handler(logger).HandleAsync(
            Command(company: "Sensitive Fleet", contact: "Iryna", email: "iryna@example.com"),
            null,
            null,
            default);

        var stored = await _context.BusinessLeads.SingleAsync(l => l.Id == result.Id);
        stored.EmailSentAtUtc.Should().BeNull();
        stored.EmailSendError.Should().Be("SMTP down");
        logger.Messages.Should().ContainSingle()
            .Which.Should().NotContain("Sensitive Fleet")
            .And.NotContain("Iryna")
            .And.NotContain("iryna@example.com")
            .And.NotContain("+380970011771");
    }

    [Fact]
    public async Task Handler_does_not_claim_delivery_when_mail_is_unconfigured()
    {
        _mail.SetupGet(m => m.IsConfigured).Returns(false);
        _mail.Setup(m => m.SendAsync(It.IsAny<BusinessLead>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await Handler().HandleAsync(Command(), null, null, default);

        (await _context.BusinessLeads.SingleAsync(l => l.Id == result.Id))
            .EmailSentAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Mail_sender_uses_separate_sales_destination_and_encodes_HTML()
    {
        var transport = new Mock<FuelFlow.SharedKernel.Notifications.Email.IEmailSender>();
        transport.SetupGet(t => t.IsConfigured).Returns(true);
        string? to = null;
        FuelFlow.SharedKernel.Notifications.Email.EmailMessage? sent = null;
        transport.Setup(t => t.SendAsync(It.IsAny<string>(), It.IsAny<FuelFlow.SharedKernel.Notifications.Email.EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, FuelFlow.SharedKernel.Notifications.Email.EmailMessage, CancellationToken>((address, mail, _) =>
            {
                to = address;
                sent = mail;
            })
            .Returns(Task.CompletedTask);

        var sender = new BusinessLeadMailSender(
            transport.Object,
            Options.Create(new FuelFlow.SharedKernel.Options.BusinessLeadMailOptions
            {
                ToEmail = "sales@example.com"
            }));

        await sender.SendAsync(new BusinessLead
        {
            Id = Guid.NewGuid(),
            CompanyName = "<Fleet>",
            ContactName = "Iryna",
            Phone = "+380970011771",
            Status = BusinessLeadStatus.New
        }, default);

        to.Should().Be("sales@example.com");
        sent!.HtmlBody.Should().Contain("&lt;Fleet&gt;").And.NotContain("<Fleet>");
    }

    private CreateBusinessLeadCommandHandler Handler(
        ILogger<CreateBusinessLeadCommandHandler>? logger = null) => new(
        _context,
        _mail.Object,
        logger ?? NullLogger<CreateBusinessLeadCommandHandler>.Instance);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private static CreateBusinessLeadCommand Command(
        string phone = "+380970011771",
        string company = "Fleet Ltd",
        string contact = "Iryna",
        string? email = null,
        int? fleetSize = null,
        string? message = null) => new(
            company,
            contact,
            phone,
            email,
            fleetSize,
            message,
            null);
}
