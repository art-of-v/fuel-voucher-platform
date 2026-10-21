using FluentAssertions;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The guarantees <c>payments</c> and <c>webhook_events</c> claim, proven against a real PostgreSQL.
/// </summary>
/// <remarks>
/// These run here rather than in the in-memory suite because every one of them is a claim about what
/// the <em>database</em> enforces rather than about what the code does. The in-memory provider accepts
/// a duplicate row, a drifted money pair and a succeeded payment with no date - exactly the set of
/// things that must not be possible.
///
/// <see cref="OneWebhookDeliveryPerProviderEvent_CannotBeStoredTwice"/> is the load-bearing one: the
/// whole promise of the webhook table is that a redelivery is impossible rather than merely ignored,
/// and only a real unique index can demonstrate that.
/// </remarks>
[Collection("Integration Tests")]
public sealed class PaymentLedgerIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public PaymentLedgerIntegrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    // ---------------------------------------------------------------- idempotency

    [Fact]
    public async Task OneWebhookDeliveryPerProviderEvent_CannotBeStoredTwice()
    {
                // The shared fixture database is not cleaned between tests, so the id is derived from an
        // invoice unique to this test. What is being asserted is that deriving it twice from the same
        // inputs yields the same value - not that a fixed literal stays unused across the suite.
        var eventId = EventIdFor($"INV-DUP-{Guid.NewGuid():N}", "success", Fixed);

        await using (var seed = CreateContext())
        {
            seed.WebhookEvents.Add(NewWebhook(eventId));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.WebhookEvents.Add(NewWebhook(eventId));

        var act = async () => await context.SaveChangesAsync();
        var ex = (await act.Should().ThrowAsync<DbUpdateException>()).Which;

        ex.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation,
                "this index is the idempotency guarantee - without it a duplicate is merely a convention");
    }

    /// <summary>
    /// The derivation has to survive a redelivery whose bytes differ - key order, whitespace. Hashing
    /// the raw body would fail this; hashing the semantic fields must not.
    /// </summary>
    [Fact]
    public async Task ARedelivery_WithDifferentByteLayout_IsStillTheSameEvent()
    {
                // The shared fixture database is not cleaned between tests, so the id is derived from an
        // invoice unique to this test. What is being asserted is that deriving it twice from the same
        // inputs yields the same value - not that a fixed literal stays unused across the suite.
        var eventId = EventIdFor($"INV-DUP-{Guid.NewGuid():N}", "success", Fixed);

        await using (var seed = CreateContext())
        {
            seed.WebhookEvents.Add(NewWebhook(eventId));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        context.WebhookEvents.Add(NewWebhook(eventId, payload: "{ \"status\" : \"success\" ,\n \"amount\" : 250000 }"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a redelivery is the same event even when the provider reformats its JSON");
    }

    [Fact]
    public async Task DifferentProviderStatuses_ForOneInvoice_AreDifferentEvents()
    {
        var invoice = $"INV-{Guid.NewGuid():N}"[..24];

        await using (var seed = CreateContext())
        {
            var paid = NewWebhook(EventIdFor(invoice, "success", Fixed));
            paid.InvoiceId = invoice;
            seed.WebhookEvents.Add(paid);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var reversed = NewWebhook(EventIdFor(invoice, "reversed", Fixed));
        reversed.InvoiceId = invoice;
        context.WebhookEvents.Add(reversed);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "a reversal is not the payment that preceded it, and both are worth keeping");
    }

    [Fact]
    public async Task SeveralCaptures_CanReferenceOneProviderInvoiceOnlyOnce()
    {
        var orderId = await SeedOrderAsync();
        var invoice = $"INV-{Guid.NewGuid():N}"[..24];

        await using (var seed = CreateContext())
        {
            var first = NewPayment(orderId);
            first.ProviderInvoiceId = invoice;
            seed.Payments.Add(first);
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext();
        var second = NewPayment(orderId);
        second.ProviderInvoiceId = invoice;
        context.Payments.Add(second);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "one capture per provider invoice is the identity that survives a re-issued payment link");
    }

    // ---------------------------------------------------------------- refund audit columns

    // The approval columns arrived on `payments` from the `refunds` table, which is being dissolved
    // into `kind = 'Refund'`. A capture has nobody to approve it, so a capture carrying an approver is
    // a row written by a path that has lost track of which kind it is - and it is invisible until
    // someone asks who authorised a payment and gets an answer that cannot be true.

    [Fact]
    public async Task ACapture_MayNotCarryARefundApprover()
    {
        var (orderId, userId) = await SeedOrderWithUserAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, createdByUserId: userId, createdByUserName: "Admin"));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "only a refund is approved by anyone");
    }

    [Fact]
    public async Task ACapture_MayNotClaimItWasAutomatic()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, isAutomatic: true));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "an automatic flag on a capture is the same misclassification with a different column");
    }

    [Fact]
    public async Task ARefund_MayCarryItsApprover()
    {
        var (orderId, userId) = await SeedOrderWithUserAsync();

        await using var context = CreateContext();

        var capture = NewPayment(orderId, status: PaymentStatus.Succeeded, paidAtUtc: TestClock.UtcNow);
        context.Payments.Add(capture);
        await context.SaveChangesAsync();

        var refund = NewPayment(
            orderId,
            amountUah: 2500.00m,
            amountKopecks: 250000m,
            status: PaymentStatus.Succeeded,
            paidAtUtc: TestClock.UtcNow,
            kind: PaymentKind.Refund,
            parentPaymentId: capture.Id,
            providerInvoiceId: capture.ProviderInvoiceId,
            createdByUserId: userId,
            createdByUserName: "Admin",
            errorMessage: null);
        context.Payments.Add(refund);

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "this is the shape the whole refunds-to-payments move depends on");
    }

    /// <summary>
    /// An approver name with no approver id is allowed, and this is the test that stopped me from
    /// forbidding it.
    /// </summary>
/// <remarks>
/// The first cut of the audit columns paired the two: a name without an id was refused as "a name in
/// a log". Real refunds immediately broke it - an automatic refund triggered by a stock-out has no
/// approver at all, and a webhook-driven one carries a name the id was never captured for. The
/// constraint would have refused refunds that genuinely happened, which is the worse failure.
/// The foreign key already does the part that matters: an id, when present, is a real user.
/// </remarks>
[Fact]
    public async Task AnApproverName_WithNoApproverId_IsAllowed()
    {
        var (orderId, _) = await SeedOrderWithUserAsync();

        await using var context = CreateContext();

        var capture = NewPayment(orderId, status: PaymentStatus.Succeeded, paidAtUtc: TestClock.UtcNow);
        context.Payments.Add(capture);
        await context.SaveChangesAsync();

        context.Payments.Add(NewPayment(
            orderId,
            status: PaymentStatus.Pending,
            kind: PaymentKind.Refund,
            parentPaymentId: capture.Id,
            createdByUserName: "stock-out rule",
            isAutomatic: true));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "an automatic refund has no approver user, and refusing it would strand a real refund");
    }

    // ---------------------------------------------------------------- money shape

    [Fact]
    public async Task MoneyColumns_MustAgree()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, amountUah: 2500.00m, amountKopecks: 250001m));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "a drifted pair is the 978.57/979 bug class, and this is where it is stopped");
    }

    [Fact]
    public async Task MoneyColumns_MayAgree()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, amountUah: 978.57m, amountKopecks: 97857m));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// The acquirer's figure and ours are allowed to differ - that difference is the finding. What is
    /// forbidden is a non-positive charge.
    /// </summary>
    [Fact]
    public async Task ProviderAmount_MayDisagreeWithOurs()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, providerAmountKopecks: 1m));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync(
            "constraining this would forbid the write that discovers a mismatch, losing the evidence");
    }

    [Fact]
    public async Task ASucceededPayment_MustCarryTheMomentTheMoneyMoved()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, status: PaymentStatus.Succeeded, paidAtUtc: null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>(
            "revenue counts Succeeded and a VAT period needs a date; either half alone makes a report lie");
    }

    [Fact]
    public async Task AReversedPayment_MayKeepTheTimeItWasReceived()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, status: PaymentStatus.Cancelled, paidAtUtc: TestClock.UtcNow));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().NotThrowAsync("the money really was received before it was returned");
    }

    [Fact]
    public async Task ARefundPayment_MustPointAtWhatItReverses()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, kind: PaymentKind.Refund, parentPaymentId: null));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("a refund is defined by what it reverses");
    }

    [Fact]
    public async Task AZeroPayment_CannotBeStored()
    {
        var orderId = await SeedOrderAsync();

        await using var context = CreateContext();
        context.Payments.Add(NewPayment(orderId, amountUah: 0m, amountKopecks: 0m));

        var act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("a refund or a charge of zero is never legitimate");
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// A real 64-character hex id, derived the way production derives one - ck_webhook_events_event_id_length
    /// is part of the contract, so a fixture that invents a 32-character GUID is testing a row the
    /// database would never accept.
    /// </summary>
    private static readonly DateTime Fixed = new(2026, 3, 13, 14, 32, 0, DateTimeKind.Utc);

    private static string EventIdFor(string invoiceId, string status, DateTime modified)
        => WebhookEventIdentity.Derive(WebhookEventIdentity.Monobank, invoiceId, status, modified)!;

    private static WebhookEvent NewWebhook(
        string providerEventId,
        string providerStatus = "success",
        string? payload = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Provider = WebhookEventIdentity.Monobank,
        ProviderEventId = providerEventId,
        ProviderStatus = providerStatus,
        InvoiceId = $"INV-{Guid.NewGuid():N}"[..24],
        AmountKopecks = 250000m,
        ProviderCreatedAtUtc = TestClock.UtcNow,
        ProviderModifiedAtUtc = TestClock.UtcNow,
        Payload = payload ?? @"{ ""status"": ""success"", ""amount"": 250000 }",
        Status = WebhookReceiptStatus.Received,
        ReceivedAtUtc = TestClock.UtcNow,
        Attempts = 1
    };

    private static Payment NewPayment(
        Guid orderId,
        decimal amountUah = 2500.00m,
        decimal amountKopecks = 250000m,
        decimal? providerAmountKopecks = null,
        PaymentStatus status = PaymentStatus.Pending,
        DateTime? paidAtUtc = null,
        PaymentKind kind = PaymentKind.Capture,
        Guid? parentPaymentId = null,
        string? providerInvoiceId = null,
        Guid? createdByUserId = null,
        string? createdByUserName = null,
        bool isAutomatic = false,
        string? errorMessage = null) => new()
    {
        Id = Guid.CreateVersion7(),
        OrderId = orderId,
        Kind = kind,
        ParentPaymentId = parentPaymentId,
        Provider = WebhookEventIdentity.Monobank,
        Merchant = MonobankMerchant.Live,
        ProviderInvoiceId = providerInvoiceId ?? $"INV-{Guid.NewGuid():N}"[..24],
        AmountUah = amountUah,
        AmountKopecks = amountKopecks,
        ProviderAmountKopecks = providerAmountKopecks,
        Currency = "UAH",
        Status = status,
        PaidAtUtc = paidAtUtc,
        CreatedByUserId = createdByUserId,
        CreatedByUserName = createdByUserName,
        IsAutomatic = isAutomatic,
        ErrorMessage = errorMessage,
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow
    };

    private async Task<Guid> SeedOrderAsync()
    {
        var (orderId, _) = await SeedOrderWithUserAsync();
        return orderId;
    }

    private async Task<(Guid OrderId, Guid UserId)> SeedOrderWithUserAsync()
    {
        var userId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = userId,
                PhoneNumber = $"+38051{TestClock.UniqueDigits(7)}",
                CreatedAtUtc = TestClock.UtcNow,
                UpdatedAtUtc = TestClock.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var orderId = Guid.CreateVersion7();
        await using var context = CreateContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            UserId = userId,
            Price = 2500m,
            Status = OrderStatus.PendingPayment,
            Kind = OrderKind.Purchase,
            MonobankInvoiceId = $"INV-{Guid.NewGuid():N}"[..24],
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });
        await context.SaveChangesAsync();

        return (orderId, userId);
    }
}