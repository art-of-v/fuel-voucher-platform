using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Brands;
using FuelFlow.Features.Payments.Admin.DismissPayment;
using FuelFlow.Features.Payments.Admin.GetUnallocatedPayments;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.UnitTests.Payments;

/// <summary>
/// Closing money off the held-money worklist without touching the money.
/// </summary>
/// <remarks>
/// The two ways this could go wrong are opposite and both expensive: deleting the row would leave the
/// acquisition statement with a received capture no payment row holds — the <c>UnappliedReceipts</c>
/// alarm, which is what the last test here guards — while dismissing too much would hide money an
/// operator still has to place. So the refusals carry as much of the feature as the write does.
/// </remarks>
public sealed class DismissPaymentCommandHandlerTests : IDisposable
{
    private static readonly Guid ActorId = Guid.Parse("01a18bf8-0000-7000-8000-000000000001");

    private readonly ApplicationDbContext _context;

    public DismissPaymentCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_ShouldRefuseARowNobodyExplained(string? reason)
    {
        var payment = await SeedHeldAsync();

        var result = await Handler().HandleAsync(Command(payment.Id, reason));

        result.Success.Should().BeFalse("a dismissal nobody can explain is worse than the row");
        result.Error.Should().Be("Say why this money will never be placed.");

        var stored = await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.DismissedAtUtc.Should().BeNull();
        stored.DismissedReason.Should().BeNull();
        (await _context.Set<AuditEventOutbox>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseAReasonTheColumnCannotHold()
    {
        var payment = await SeedHeldAsync();

        var result = await Handler().HandleAsync(Command(payment.Id, new string('x', 201)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("200", "the refusal names the same limit the column carries");
        (await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id))
            .DismissedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseMoneyThatWasNeverReceived()
    {
        var payment = await SeedHeldAsync(status: PaymentStatus.Pending);

        var result = await Handler().HandleAsync(Command(payment.Id, "a test capture"));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Only succeeded money can be dismissed. This payment is Pending.");
    }

    [Fact]
    public async Task HandleAsync_ShouldRefuseMoneyThatIsAlreadyOnAnOrder()
    {
        // Money attached to an order is not held money: it has an answer, and the worklist is only
        // the rows that do not.
        var payment = await SeedHeldAsync();
        payment.OrderId = Guid.NewGuid();
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(Command(payment.Id, "a test capture"));

        result.Success.Should().BeFalse();
        result.Error.Should().Be("This payment is on an order; the worklist does not hold it.");
        (await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id))
            .DismissedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerNotFoundForAPaymentThatDoesNotExist()
    {
        var result = await Handler().HandleAsync(Command(Guid.NewGuid(), "a test capture"));

        result.Found.Should().BeFalse();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_ShouldTakeTheMoneyOffTheWorklistAndLeaveTheMoneyBooked()
    {
        var payment = await SeedHeldAsync();
        var handler = Handler();

        var before = await new GetUnallocatedPaymentsQueryHandler(_context)
            .HandleAsync(new GetUnallocatedPaymentsQuery(1, 50));
        before.Total.Should().Be(1);

        var result = await handler.HandleAsync(Command(payment.Id, "  1 ₴ test capture  "));

        result.Success.Should().BeTrue();

        var stored = await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.DismissedAtUtc.Should().NotBeNull();
        stored.DismissedReason.Should().Be("1 ₴ test capture", "the reason is trimmed, not rewritten");
        stored.DismissedByUserId.Should().Be(ActorId);

        // The money itself did not move: same status, same amount, still nobody's order. The
        // dismissal answers a question about the queue, not about the books.
        stored.Status.Should().Be(PaymentStatus.Succeeded);
        stored.AmountUah.Should().Be(1m);
        stored.OrderId.Should().BeNull();

        var after = await new GetUnallocatedPaymentsQueryHandler(_context)
            .HandleAsync(new GetUnallocatedPaymentsQuery(1, 50));
        after.Total.Should().Be(0);
        after.Data.Should().BeEmpty();

        var audit = await _context.Set<AuditEventOutbox>().SingleAsync();
        audit.AggregateType.Should().Be("Payment");
        audit.AggregateId.Should().Be(payment.Id.ToString());
        audit.EventType.Should().Be("PaymentDismissed");
        audit.ChangedByUserId.Should().Be(ActorId);
        audit.Summary.Should().Contain("1 ₴ test capture");
    }

    [Fact]
    public async Task HandleAsync_ShouldAnswerTheSecondPressWithoutRewritingTheReason()
    {
        var payment = await SeedHeldAsync();
        var handler = Handler();
        (await handler.HandleAsync(Command(payment.Id, "first reason"))).Success.Should().BeTrue();

        var second = await handler.HandleAsync(Command(payment.Id, "a different reason"));

        // A double-click is not a failure, and the first reason is the one that happened.
        second.Found.Should().BeTrue();
        second.Success.Should().BeTrue();

        var stored = await _context.Payments.AsNoTracking().FirstAsync(p => p.Id == payment.Id);
        stored.DismissedReason.Should().Be("first reason");
        (await _context.Set<AuditEventOutbox>().CountAsync()).Should().Be(1,
            "a second press changed nothing, so it must describe nothing");
    }

    /// <summary>
    /// The statement still matched the capture after the dismissal; that is the whole reason the row
    /// stays. If the money were deleted instead, this same invoice would come back as an unbooked
    /// capture and the close would read as a bank difference nobody can explain (planning #780).
    /// </summary>
    [Fact]
    public async Task A_Dismissed_Capture_Is_Still_Money_The_Statement_Matched()
    {
        var payment = await SeedHeldAsync();
        _context.MonobankStatementEntries.Add(new MonobankStatementEntry
        {
            Id = Guid.NewGuid(),
            Merchant = MonobankMerchant.Live,
            StatementId = $"INV-{payment.Id:N}",
            InvoiceId = payment.ProviderInvoiceId,
            AmountKopecks = 100,
            Status = "success",
            AcquirerCreatedAtUtc = TestClock.UtcNow,
            FirstSeenAtUtc = TestClock.UtcNow,
        });
        await _context.SaveChangesAsync();

        var result = await Handler().HandleAsync(Command(payment.Id, "a test capture"));
        result.Success.Should().BeTrue();

        var unbooked = await UnappliedReceipts.FindAsync(
            _context, MonobankMerchant.Live,
            TestClock.UtcNow.AddDays(-1), TestClock.UtcNow.AddDays(1),
            CancellationToken.None);

        unbooked.Should().BeEmpty("the payment row still holds the statement's invoice");
    }

    private DismissPaymentCommandHandler Handler() => new(
        _context,
        new AuditEventService(_context),
        NullLogger<DismissPaymentCommandHandler>.Instance);

    private static DismissPaymentCommand Command(Guid id, string? reason) =>
        new(id, reason, ActorId, "Artem Vashchuk");

    private async Task<Payment> SeedHeldAsync(PaymentStatus status = PaymentStatus.Succeeded)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            UserId = null,
            OrderId = null,
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = $"INV-{Guid.NewGuid():N}",
            AmountUah = 1m,
            AmountKopecks = 100m,
            Currency = "UAH",
            Status = status,
            PaidAtUtc = TestClock.UtcNow,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }
}
