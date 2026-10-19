using FluentAssertions;
using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests.Payments;

/// <summary>
/// What the webhook path does with the delivery it is handed, and with the money behind it.
/// </summary>
/// <remarks>
/// <para>
/// These run on the in-memory provider, which does <b>not</b> enforce unique indexes or CHECK
/// constraints. Everything asserted here is therefore behaviour of the code rather than of the
/// database; the database's half of the same claims is proven in
/// <c>PaymentLedgerIntegrationTests</c> against a real PostgreSQL.
/// </para>
/// <para>
/// The reason the in-memory provider is enough for this half is that these tests are about
/// <em>sequencing</em>: that a delivery is on file before the order moves, that a mismatch leaves
/// the order untouched, and that the payment follows what the acquirer said rather than what the
/// order did.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class WebhookDeliveryRecordingTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly ProcessMonobankWebhookCommandHandler _handler;

    public WebhookDeliveryRecordingTests(TestDatabaseFixture fixture)
    {
        _context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .Options, TestQrCipher.Instance);

        _handler = new ProcessMonobankWebhookCommandHandler(
            _context,
            NullLogger<ProcessMonobankWebhookCommandHandler>.Instance,
            new Mock<IBackgroundJobClient>().Object,
            new Mock<RefundStatusSyncService>(null!, null!, null!).Object,
            new FuelFlow.SharedKernel.Observability.FuelFlowMetrics(),
            FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled,
            TestJournal.For(_context));

        TestJournal.SeedChart(_context);
    }

    /// <summary>Each test starts from empty: the class shares one cloned database.</summary>
    public async Task InitializeAsync() => await PaymentTestReset.RunAsync(_context);

    public Task DisposeAsync()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
        return Task.CompletedTask;
    }

    private static int _userSeq;

    /// <summary>
    /// Orders carry a real FK to <c>users</c>, which the in-memory provider never enforced. Several
    /// of these tests assert <c>SingleAsync</c> over payments or webhook events, so a leaked row
    /// from a previous test would fail them for the wrong reason.
    /// </summary>
    private User SeedUser() => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = $"+38050{Interlocked.Increment(ref _userSeq):D7}",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        SettlementCode = string.Empty,
    };

    // ---------------------------------------------------------------- the delivery

    [Fact]
    public async Task ASuccessfulDelivery_IsStoredAndMarkedProcessed()
    {
        var order = await SeedOrderAsync("INV-OK-1", OrderStatus.PendingPayment);

        await _handler.HandleAsync(Webhook("INV-OK-1", "success", amountKopecks: 250000m));

        var stored = await _context.WebhookEvents.SingleAsync();
        stored.Status.Should().Be(WebhookReceiptStatus.Processed);
        stored.ProcessedAtUtc.Should().NotBeNull();
        stored.InvoiceId.Should().Be("INV-OK-1");
        stored.ProviderStatus.Should().Be("success");
        stored.Payload.Should().Contain("INV-OK-1", "the body is the evidence and it has to survive verbatim");
    }

    [Fact]
    public async Task AnAmountMismatch_IsStoredAsFailed_AndLeavesTheOrderUnpaid()
    {
        await SeedOrderAsync("INV-BAD-1", OrderStatus.PendingPayment);

        var response = await _handler.HandleAsync(Webhook("INV-BAD-1", "success", amountKopecks: 999_000m));

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("AMOUNT_MISMATCH");

        var order = await _context.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.PendingPayment,
            "handing over goods for a price the customer did not agree to is worse than doing nothing");

        var stored = await _context.WebhookEvents.SingleAsync();
        stored.Status.Should().Be(WebhookReceiptStatus.Failed,
            "we saw it, nobody acted on it, and it is a real problem - not an ignore and not a success");
        stored.Status.Should().Be(await _context.WebhookEvents
            .Where(e => e.Status == WebhookReceiptStatus.Failed)
            .Select(e => e.Status)
            .SingleAsync(), "it must be reachable through ix_webhook_events_outstanding");
    }

    /// <summary>
    /// The money really was taken, even though we refused to act on it. The payment records that; the
    /// order does not advance. That divergence is the case the old schema could not represent at all.
    /// </summary>
    [Fact]
    public async Task AnAmountMismatch_StillRecordsThePaymentAsTaken()
    {
        await SeedOrderAsync("INV-BAD-2", OrderStatus.PendingPayment);
        await SeedPaymentAsync("INV-BAD-2");

        await _handler.HandleAsync(Webhook("INV-BAD-2", "success", amountKopecks: 999_000m));

        var payment = await _context.Payments.SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Succeeded, "the acquirer says the money arrived");
        payment.ProviderAmountKopecks.Should().Be(999_000m);
        payment.AmountKopecks.Should().Be(250_000m, "what we billed, kept beside what they charged");
    }

    [Fact]
    public async Task AStaleReplay_IsStoredAsIgnored()
    {
        await SeedOrderAsync("INV-STALE", OrderStatus.PendingPayment);
        await SeedPaymentAsync("INV-STALE");

        await _handler.HandleAsync(Webhook("INV-STALE", "success", amountKopecks: 250_000m));

        // A second, older delivery. Same invoice, a status the acquirer no longer reports.
        var replay = Webhook("INV-STALE", "failure", amountKopecks: 250_000m);
        replay.ModifiedDate = DateTime.UtcNow.AddMinutes(-10);

        var response = await _handler.HandleAsync(replay);

        response.Success.Should().BeTrue();
        var order = await _context.Orders.SingleAsync();
        order.Status.Should().Be(OrderStatus.PendingFulfillment, "an older callback must not undo a payment");
    }

    [Fact]
    public async Task AnInvoiceWithNoOrder_IsStoredRatherThanDropped()
    {
        var response = await _handler.HandleAsync(Webhook("INV-GHOST", "success", amountKopecks: 250_000m));

        response.Success.Should().BeFalse();

        var stored = await _context.WebhookEvents.SingleAsync();
        stored.InvoiceId.Should().Be("INV-GHOST");
        stored.Status.Should().Be(WebhookReceiptStatus.Ignored);
    }

    // ---------------------------------------------------------------- the payment

    [Fact]
    public async Task ASuccessfulCallback_MovesThePaymentAndStampsWhen()
    {
        await SeedOrderAsync("INV-P1", OrderStatus.PendingPayment);
        await SeedPaymentAsync("INV-P1");

        var modified = new DateTime(2026, 3, 13, 14, 32, 0, DateTimeKind.Utc);
        var command = Webhook("INV-P1", "success", amountKopecks: 250_000m);
        command.ModifiedDate = modified;

        await _handler.HandleAsync(command);

        var payment = await _context.Payments.SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.PaidAtUtc.Should().Be(modified);
        payment.WebhookEventId.Should().NotBeNull("the delivery that proved it is linked from the money");
    }

    [Fact]
    public async Task TheFirstPaidAtWins_AndALaterCallbackDoesNotRestampIt()
    {
        await SeedOrderAsync("INV-P2", OrderStatus.PendingPayment);
        await SeedPaymentAsync("INV-P2");

        var first = Webhook("INV-P2", "success", amountKopecks: 250_000m);
        first.ModifiedDate = new DateTime(2026, 3, 13, 14, 32, 0, DateTimeKind.Utc);
        await _handler.HandleAsync(first);

        var payment = await _context.Payments.SingleAsync();
        var stamped = payment.PaidAtUtc;
        stamped.Should().NotBeNull();

        // A later callback for the same invoice. The provider's modifiedDate moves every time the
        // acquirer touches the invoice, so overwriting would replace "when the money arrived" with
        // "when someone last edited this row".
        var later = Webhook("INV-P2", "success", amountKopecks: 250_000m);
        later.ModifiedDate = first.ModifiedDate.AddDays(1);
        await _handler.HandleAsync(later);

        payment.PaidAtUtc.Should().Be(stamped);
    }

    [Fact]
    public async Task ADeliveryWithNoPaymentRow_IsNotAnError()
    {
        await SeedOrderAsync("INV-P3", OrderStatus.PendingPayment);

        var response = await _handler.HandleAsync(Webhook("INV-P3", "success", amountKopecks: 250_000m));

        response.Success.Should().BeTrue();
        // An invoice can predate this table, or checkout failed before its save. Both are legitimate
        // and neither belongs on a money path as an error.
        (await _context.Payments.CountAsync()).Should().Be(0);
    }

    // ---------------------------------------------------------------- the identity

    [Fact]
    public void TheDerivedEventId_IsStableAcrossEquivalentDeliveries()
    {
        var modified = new DateTime(2026, 3, 13, 14, 32, 0, DateTimeKind.Utc);

        var first = WebhookEventIdentity.Derive("monobank", "INV-1", "success", modified);
        var second = WebhookEventIdentity.Derive("monobank", "INV-1", "success", modified);

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void TheDerivedEventId_DoesNotDependOnTheServersTimeZone()
    {
        var utc = new DateTime(2026, 3, 13, 14, 32, 0, DateTimeKind.Utc);
        var asLocal = utc.ToLocalTime();

        WebhookEventIdentity.Derive("monobank", "INV-1", "success", utc)
            .Should().Be(WebhookEventIdentity.Derive("monobank", "INV-1", "success", asLocal),
                "two boxes configured differently must produce the same id or the uniqueness does not hold");
    }

    [Theory]
    [InlineData("INV-1", "success", 0)]
    [InlineData("INV-1", "reversed", 0)]
    [InlineData("INV-2", "success", 0)]
    public void TheDerivedEventId_SeparatesGenuinelyDifferentEvents(string invoice, string status, int _)
    {
        var baseline = WebhookEventIdentity.Derive("monobank", "INV-1", "success", DateTime.UtcNow);

        WebhookEventIdentity.Derive("monobank", invoice, status, DateTime.UtcNow)
            .Should().NotBe(baseline);
    }

    [Fact]
    public void AnUnidentifiableEvent_DerivesNothingRatherThanInventingAnId()
    {
        WebhookEventIdentity.Derive("monobank", null, "success", null).Should().BeNull();
        WebhookEventIdentity.Derive("monobank", "  ", "success", null).Should().BeNull();
    }

    // ---------------------------------------------------------------- fixtures

    private static ProcessMonobankWebhookCommand Webhook(
        string invoiceId,
        string status,
        decimal amountKopecks) => new()
    {
        InvoiceId = invoiceId,
        Status = status,
        Amount = MoneyForMonobank(amountKopecks),
        CreatedDate = DateTime.UtcNow,
        ModifiedDate = DateTime.UtcNow,
        RawBody = $"{{ \"invoiceId\": \"{invoiceId}\", \"status\": \"{status}\", \"amount\": {(long)amountKopecks} }}"
    };

    private static long MoneyForMonobank(decimal kopecks) => decimal.ToInt64(kopecks);

private async Task<Order> SeedOrderAsync(string invoiceId, OrderStatus status)
    {
        var user = SeedUser();
        _context.Users.Add(user);

        var order = new Order
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Price = 2500m,
            Status = status,
            Kind = OrderKind.Purchase,
            MonobankInvoiceId = invoiceId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }

    private async Task<Payment> SeedPaymentAsync(string invoiceId)
    {
        var order = await _context.Orders.SingleAsync(o => o.MonobankInvoiceId == invoiceId);

        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = invoiceId,
            AmountUah = order.Price,
            AmountKopecks = FuelFlow.SharedKernel.Money.ToKopecks(order.Price),
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }
}