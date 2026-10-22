using System.Linq.Expressions;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.API.Features.Monobank.ProcessWebhook;
using FuelFlow.API.Features.Orders.RefundOrder;
using FuelFlow.SharedKernel;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Features.Monobank.ProcessWebhook;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Payments;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Time;
using FuelFlow.Testing;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static FuelFlow.SharedKernel.Domain.FuelIdMapper;

namespace FuelFlow.IntegrationTests.Monobank;

/// <summary>
/// The Monobank webhook handler against a real Postgres, not the in-memory provider.
/// </summary>
/// <remarks>
/// A settled payment now posts to the double-entry journal, and the journal takes its entry number
/// with <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> over <c>gen_random_uuid()</c> - neither
/// of which the EF in-memory provider runs. Moving here buys the real engine: foreign keys,
/// triggers, transactions and constraints, which is what a posting path deserves to be tested on.
/// It also buys back an old blind spot: in-memory never enforced <c>FK_orders_users_user_id</c>, so
/// these tests used to seed orders with a user id that production's database would have refused.
/// </remarks>
[Collection("Integration Tests")]
public sealed class ProcessMonobankWebhookCommandHandlerTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly ProcessMonobankWebhookCommandHandler _handler;
    private readonly Mock<IBackgroundJobClient> _backgroundJobClientMock;

    public ProcessMonobankWebhookCommandHandlerTests(TestDatabaseFixture fixture)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _backgroundJobClientMock = new Mock<IBackgroundJobClient>();

        _handler = new ProcessMonobankWebhookCommandHandler(
            _context,
            new Mock<ILogger<ProcessMonobankWebhookCommandHandler>>().Object,
            _backgroundJobClientMock.Object,
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
        return Task.CompletedTask;
    }

    private static int _userSeq;

    /// <summary>
    /// Creates the customer behind an order. Seeded through the context so the insert order is the
    /// database's to decide, rather than the test's.
    /// </summary>
    private User SeedUser() => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = $"+38050{Interlocked.Increment(ref _userSeq):D7}",
        CreatedAtUtc = TestClock.UtcNow,
        UpdatedAtUtc = TestClock.UtcNow,
        SettlementCode = string.Empty,
    };

    private Order BuildOrder(string invoiceId, OrderStatus status)
    {
        var user = SeedUser();
        _context.Users.Add(user);

return new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Price = 2500,
            Status = status,
            MonobankInvoiceId = invoiceId,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
LineItems =
            {
                new OrderLineItem
                {
                    Id = Guid.NewGuid(),
                    BrandId = BrandIds.ForSlug("okko"),
                    Brand = "okko",
                    FuelTypeId = ToUuid("okko-95"),
                    Liters = 50,
                    Quantity = 1,
                    UnitPrice = 2500,
                    LineTotal = 2500
                }
            }
        };
    }

    private static ProcessMonobankWebhookCommand WebhookCommand(string invoiceId, string status) => new()
    {
        InvoiceId = invoiceId,
        Status = status,
        Amount = 250000,
        CreatedDate = TestClock.UtcNow,
        ModifiedDate = TestClock.UtcNow
    };

    [Fact]
    public async Task ProcessWebhook_Success_ShouldSetPendingFulfillment()
    {
        var order = BuildOrder("INV-001", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-001", "success"));

        response.Success.Should().BeTrue();
        response.OrderId.Should().Be(order.Id.ToString());
        response.PreviousStatus.Should().Be(OrderStatus.PendingPayment.ToString());
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        var outboxEvent = await _context.OutboxEvents.SingleOrDefaultAsync(e => e.EventType == OutboxEventType.OrderCreated);
        outboxEvent.Should().NotBeNull();
        outboxEvent!.Processed.Should().BeFalse();
        outboxEvent.Payload.Should().Contain(order.Id.ToString());

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhook_Failure_ShouldCancelOrder()
    {
        var order = BuildOrder("INV-002", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-002", "failure"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.Cancelled.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_ShouldReturnSuccessFalse_WhenOrderNotFound()
    {
        var response = await _handler.HandleAsync(WebhookCommand("INV-MISSING", "success"));

        response.Success.Should().BeFalse();
        response.OrderId.Should().BeNull();
        response.Message.Should().Contain("not found");
        response.ErrorCode.Should().Be("NOT_FOUND");

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_OnSoftDeletedOrder_ShouldRestoreAndFulfill()
    {
        // #23: a customer can swipe-delete an unpaid checkout (soft delete) while its Monobank
        // invoice stays live. A later "success" must still find the order, restore it (un-hide),
        // and fulfill — never leave the customer charged with no voucher.
        var order = BuildOrder("INV-DEL", OrderStatus.PendingPayment);
        order.IsDeleted = true;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-DEL", "success"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        // FindAsync bypasses the global !IsDeleted query filter, so we can read the restored row back.
        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.IsDeleted.Should().BeFalse();
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessWebhook_Success_WithAmountMismatch_ShouldNotTransition()
    {
        var order = BuildOrder("INV-AMT", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-AMT", "success");
        command.Amount = 1; // does not match order.Price * 100

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("AMOUNT_MISMATCH");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
        updated.LastWebhookModifiedDateUtc.Should().BeNull();

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_StaleModifiedDate_ShouldAcknowledgeWithoutTransition()
    {
        var order = BuildOrder("INV-STALE", OrderStatus.PendingFulfillment);
        order.LastWebhookModifiedDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-STALE", "success");
        command.ModifiedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());
        response.Message.Should().Contain("Stale");

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_DuplicateSuccess_WhenAlreadyPendingFulfillment_ShouldNotReEnqueue()
    {
        var order = BuildOrder("INV-DUP", OrderStatus.PendingFulfillment);
        order.LastWebhookModifiedDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-DUP", "success");
        command.ModifiedDate = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());
        response.Message.Should().Contain("Duplicate");

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_OnFulfilledOrder_ShouldBeRejectedAsIllegalTransition()
    {
        var order = BuildOrder("INV-FUL", OrderStatus.Fulfilled);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-FUL", "success"));

        response.Success.Should().BeTrue();
        response.Message.Should().Contain("Illegal transition");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Fulfilled);

        // Refusing the transition must not also throw away the fact that the money arrived.
        // The fixture starts with no recorded payment status; the webhook now persists what the
        // brand said, so the order stops looking unpaid.
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_OnCancelledOrder_ShouldRecordPaymentTruthWithoutIssuingGoods()
    {
        // The production incident, reproduced.
        //
        // A `failure` cancelled this order, but the invoice stays payable for its whole validity
        // window and the customer paid it from a stale browser tab. The `success` arrives for an
        // order that is already terminal, so issuing goods would be wrong - and the old code
        // simply ACKed, leaving monobank_status on the last applied transition. The order then
        // read as unpaid: revenue excluded it, reconciliation never polled it (PendingPayment
        // only), and nothing on any screen pointed an operator at the trapped money.
        var order = BuildOrder("INV-LATE", OrderStatus.Cancelled);
        order.MonobankStatus = MonobankStatus.Failure;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-LATE", "success"));

        response.Success.Should().BeTrue();
        response.Message.Should().Contain("Illegal transition");

        var updated = await _context.Orders.FindAsync(order.Id);

        // Terminal stays terminal. No goods for a cancelled order.
        updated!.Status.Should().Be(OrderStatus.Cancelled);
        _context.OutboxEvents.Should().BeEmpty();
        _context.FuelVouchers.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);

        // But the payment truth is on record, so the order is discoverable as paid and the
        // manual refund path is reachable from the UI without querying Monobank by hand.
        updated.MonobankStatus.Should().Be(MonobankStatus.Success);
        RefundOrderCommandHandler.ComputeRefundAmountKopecks(updated).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ProcessWebhook_RepeatedSuccess_OnCancelledOrder_ShouldNotChurnTimestamps()
    {
        // Monobank retries webhooks. Once the payment truth is recorded there is nothing left to
        // change, so a replay must not rewrite history or bump updated_at_utc on every delivery.
        var order = BuildOrder("INV-REPLAY", OrderStatus.Cancelled);
        order.MonobankStatus = MonobankStatus.Success;
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var before = (await _context.Orders.FindAsync(order.Id))!.UpdatedAtUtc;

        await _handler.HandleAsync(WebhookCommand("INV-REPLAY", "success"));

        var after = await _context.Orders.FindAsync(order.Id);
        after!.MonobankStatus.Should().Be(MonobankStatus.Success);
        after.UpdatedAtUtc.Should().Be(before);
    }

    [Fact]
    public async Task ProcessWebhook_Reversed_ShouldCancelOrder()
    {
        var order = BuildOrder("INV-REV", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-REV", "reversed"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.Cancelled.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessWebhook_Expired_ShouldCancelOrder()
    {
        // An expired invoice is terminal: its validity window elapsed with no payment. Cancelling
        // here stops the reconciliation poll from chasing it every cycle and clears admin views.
        var order = BuildOrder("INV-EXP", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-EXP", "expired"));

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.Cancelled.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.Cancelled);

        _context.OutboxEvents.Should().BeEmpty();
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessWebhook_Success_ShouldRecordLastWebhookTracking()
    {
        var order = BuildOrder("INV-TRACK", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(WebhookCommand("INV-TRACK", "success"));

        response.Success.Should().BeTrue();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.LastWebhookProcessedAtUtc.Should().NotBeNull();
        updated.LastWebhookModifiedDateUtc.Should().NotBeNull();
    }
    [Fact]
    public async Task ProcessWebhook_Success_Books_The_Money_In_The_Journal()
    {
        // The first posting the system has ever made, asserted end to end: a settled payment must
        // leave two balanced entries - the gross and the VAT inside it - or the books are silently
        // short and nobody finds out until the month closes.
        var order = BuildOrder("INV-JRNL", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(WebhookCommand("INV-JRNL", "success"));

        var entries = await _context.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.SourceId == order.Id)
            .ToListAsync();

        entries.Should().HaveCount(2, "the gross and the VAT are separate facts and post separately");
        entries.Should().OnlyContain(e => e.Status == JournalEntryStatus.Posted);
        entries.Should().OnlyContain(e => e.Lines.Sum(l => l.DebitUah) == e.Lines.Sum(l => l.CreditUah));

        // 2500 gross at 20% carries 416.67 of VAT, so the revenue leg is 2083.33.
        var gross = entries.Single(e => e.SourceEvent == "payment.received");
        gross.Lines.Sum(l => l.DebitUah).Should().Be(2500m);
        gross.Lines.Single(l => l.DebitUah > 0).Account!.Code.Should().Be("311");
        gross.Lines.Single(l => l.CreditUah > 0).Account!.Code.Should().Be("681");

        var vat = entries.Single(e => e.SourceEvent == "payment.received.vat");
        vat.Lines.Sum(l => l.DebitUah).Should().Be(416.67m);
        vat.Lines.Single(l => l.DebitUah > 0).Account!.Code.Should().Be("703");
        vat.Lines.Single(l => l.CreditUah > 0).Account!.Code.Should().Be("643");
    }

    [Fact]
    public async Task ProcessWebhook_Replayed_After_Success_Does_Not_Book_The_Money_Twice()
    {
        // Both arrival paths converge here, so idempotency is what keeps a recovered payment from
        // being counted twice. This is the property the whole "hook once, anywhere" design rests on.
        var order = BuildOrder("INV-REPLAY2", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await _handler.HandleAsync(WebhookCommand("INV-REPLAY2", "success"));
        var replay = await _handler.HandleAsync(WebhookCommand("INV-REPLAY2", "success"));

        replay.Message.Should().Contain("already recorded");

        var count = await _context.JournalEntries.CountAsync(e => e.SourceId == order.Id);
        count.Should().Be(2, "a replay returns the existing entries rather than adding more");

        // The new guard has to keep refusing exactly this: a delivery that already moved the money.
        var delivery = await _context.WebhookEvents.AsNoTracking().SingleAsync(e => e.InvoiceId == "INV-REPLAY2");
        delivery.Status.Should().Be(WebhookReceiptStatus.Processed);
        delivery.Attempts.Should().Be(2, "the replay is still counted, even though it is refused");
        delivery.ProcessingStartedAtUtc.Should().BeNull("no attempt is running once the delivery is settled");
    }

    // ---------------------------------------------------------------- the money step is one unit of work

    [Fact]
    public async Task AJournalFailure_LeavesTheOrderWhereItWas_AndQueuesNoFulfilment()
    {
        // C-13. The order's status, the outbox event that hands it to fulfilment, the journal
        // entries and the payment row are ONE unit of work. A closed accounting period is how the
        // ledger refuses a posting, and the refusal has to undo the whole step: an order that is
        // PendingFulfillment with a queued outbox event and no entry in the books is fuel issued
        // for money nobody recorded - which is the hole that is found at month end.
        var order = BuildOrder("INV-ATOMIC", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await SeedPaymentAsync(order);
        CloseTheAccountingPeriod(order.UserId);

        var act = async () => await _handler.HandleAsync(WebhookCommand("INV-ATOMIC", "success"));

        await act.Should().ThrowAsync<LedgerPeriodClosedException>(
            "a closed month refuses the posting, and the refusal is what the transaction is for");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment, "the order goes back to where it was");
        updated.MonobankStatus.Should().BeNull("and it does not claim the money either");
        updated.LastWebhookModifiedDateUtc.Should().BeNull();

        (await _context.OutboxEvents.CountAsync()).Should().Be(0,
            "no event hands a rolled-back order to fulfilment");
        (await _context.JournalEntries.CountAsync()).Should().Be(0, "nothing was booked");
        (await _context.Fulfillments.CountAsync()).Should().Be(0, "no fuel was issued");

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);
    }

    [Fact]
    public async Task AJournalFailure_LeavesThePaymentAndTheOrderAgreeing()
    {
        // The other half of the same promise: the payment record and the order must not disagree.
        // "The money was taken and the order did not advance" is money without fuel; "the order
        // advanced and the money was not recorded" is fuel without money. Both are what a
        // half-applied money step looks like, and both are refused by the same transaction.
        var order = BuildOrder("INV-AGREE", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await SeedPaymentAsync(order);
        CloseTheAccountingPeriod(order.UserId);

        var act = async () => await _handler.HandleAsync(WebhookCommand("INV-AGREE", "success"));
        await act.Should().ThrowAsync<LedgerPeriodClosedException>();

        var payment = await _context.Payments.SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending,
            "the books refused the money, so nothing records it as taken");
        payment.PaidAtUtc.Should().BeNull();

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment);
        updated.MonobankStatus.Should().BeNull("the order does not claim the money either");

        // The delivery is on file as failed, with the amount the acquirer charged, so the retry -
        // Monobank's or the reconciliation poller's - has the evidence it needs.
        var delivery = await _context.WebhookEvents.SingleAsync();
        delivery.Status.Should().Be(WebhookReceiptStatus.Failed);
        delivery.Error.Should().Contain("closed");
    }

    [Fact]
    public async Task ALaterDelivery_AfterAJournalFailure_RecoversTheOrderAndBooksTheMoneyOnce()
    {
        // The point of rolling back rather than half-applying: nothing was written, so the next
        // delivery for this invoice finds an order that is still waiting for its fuel, and it books
        // the money exactly once. Nothing to undo and nothing to double.
        //
        // A later delivery, not the same one: it carries a different modifiedDate, so it is a new
        // identity and takes the ordinary insert path. This is what the reconciliation poller sends
        // when the invoice has moved on since the failed callback. The retry of the very same event
        // is the other case, covered by AFailedDelivery_IsProcessedAgain_.
        var order = BuildOrder("INV-RETRY", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await SeedPaymentAsync(order);
        CloseTheAccountingPeriod(order.UserId);

        var act = async () => await _handler.HandleAsync(WebhookCommand("INV-RETRY", "success"));
        await act.Should().ThrowAsync<LedgerPeriodClosedException>();

        // The accountant opens the month again.
        var period = await _context.AccountingPeriods.SingleAsync();
        period.Status = PeriodStatus.Open;
        period.ClosedAtUtc = null;
        period.ClosedByUserId = null;
        await _context.SaveChangesAsync();

        var retry = WebhookCommand("INV-RETRY", "success");
        retry.ModifiedDate = TestClock.UtcNow.AddMinutes(5);

        var response = await _handler.HandleAsync(retry);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await _context.OutboxEvents.CountAsync()).Should().Be(1, "the handover to fulfilment happens once");
        (await _context.JournalEntries.CountAsync(e => e.SourceId == order.Id)).Should().Be(2,
            "the gross and the VAT are posted once, not twice");

        var payment = await _context.Payments.SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Succeeded);

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task AFailedDelivery_IsProcessedAgain_WhenTheAcquirerRetriesTheSameEvent()
    {
        // #634. The unique index refuses a second row, not a retry of the first. Before the lease,
        // this redelivery - byte for byte the same event - was acknowledged and dropped, so the
        // order the rollback had just restored to PendingPayment could never be paid: the row was
        // Failed and nothing would ever act on it again.
        var order = BuildOrder("INV-SAME-RETRY", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await SeedPaymentAsync(order);
        CloseTheAccountingPeriod(order.UserId);

        var delivery = WebhookCommand("INV-SAME-RETRY", "success");

        var act = async () => await _handler.HandleAsync(delivery);
        await act.Should().ThrowAsync<LedgerPeriodClosedException>();

        var failed = await _context.WebhookEvents.SingleAsync();
        failed.Status.Should().Be(WebhookReceiptStatus.Failed);
        failed.ProcessingStartedAtUtc.Should().BeNull("a finished attempt releases its lease");

        // The accountant opens the month again, and Monobank redelivers the very same event.
        var period = await _context.AccountingPeriods.SingleAsync();
        period.Status = PeriodStatus.Open;
        period.ClosedAtUtc = null;
        period.ClosedByUserId = null;
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(delivery);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);
        updated.LastWebhookModifiedDateUtc.Should().Be(delivery.ModifiedDate, "the retry is the delivery that advanced it");

        (await _context.OutboxEvents.CountAsync()).Should().Be(1, "the handover to fulfilment happens once");
        (await _context.JournalEntries.CountAsync(e => e.SourceId == order.Id)).Should().Be(2,
            "the gross and the VAT are posted once, not twice");
        (await _context.Payments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Succeeded);

        var retried = await _context.WebhookEvents.AsNoTracking().SingleAsync();
        retried.Status.Should().Be(WebhookReceiptStatus.Processed);
        retried.Attempts.Should().Be(2, "the retry was counted on the row it took over");
        retried.ProcessingStartedAtUtc.Should().BeNull();
        retried.ProcessedAtUtc.Should().NotBeNull();

        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ARedelivery_WhileAnAttemptHoldsTheLease_IsAcknowledgedButNotProcessed()
    {
        // Two deliveries of the same event can overlap: Monobank retries while the first request is
        // still running. The second must not run the money step a second time. The fresh lease is
        // what tells it "in flight", as opposed to "the process that took it died".
        var order = BuildOrder("INV-INFLIGHT", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var command = WebhookCommand("INV-INFLIGHT", "success");
        var inFlight = await RecordArrivalAsync(command);

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue(
            "Monobank must not be told to retry a delivery that is already being handled");
        response.Message.Should().Contain("already being processed");

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingPayment, "the running attempt has not decided anything yet");

        (await _context.OutboxEvents.CountAsync()).Should().Be(0);
        (await _context.JournalEntries.CountAsync()).Should().Be(0);
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Never);

        var receipt = await _context.WebhookEvents.AsNoTracking().SingleAsync();
        receipt.Status.Should().Be(WebhookReceiptStatus.Received, "the running attempt is still the owner");
        receipt.Attempts.Should().Be(2, "the refused delivery is still counted, and the lease is not touched");
        receipt.ProcessingStartedAtUtc.Should().Be(inFlight.ProcessingStartedAtUtc, "the owner keeps its lease");
    }

    [Fact]
    public async Task AReceivedDelivery_WhoseAttemptDied_IsRetriedOnceTheLeaseExpires()
    {
        // A process that dies mid-attempt leaves Received with its lease still set, and nobody
        // answers Monobank. Without the expiry the row would look in flight forever; with it the
        // next delivery - a redelivery or the reconciliation poll - takes the same row and finishes
        // the work, which is the state the issue asked the design to answer.
        var order = BuildOrder("INV-CRASH", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        await SeedPaymentAsync(order);

        var command = WebhookCommand("INV-CRASH", "success");
        var crashed = await RecordArrivalAsync(command);

        crashed.ProcessingStartedAtUtc = TestClock.UtcNow.AddMinutes(-6);
        await _context.SaveChangesAsync();

        var response = await _handler.HandleAsync(command);

        response.Success.Should().BeTrue();
        response.NewStatus.Should().Be(OrderStatus.PendingFulfillment.ToString());

        var updated = await _context.Orders.FindAsync(order.Id);
        updated!.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await _context.OutboxEvents.CountAsync()).Should().Be(1);
        (await _context.JournalEntries.CountAsync(e => e.SourceId == order.Id)).Should().Be(2);

        var recovered = await _context.WebhookEvents.AsNoTracking().SingleAsync();
        recovered.Status.Should().Be(WebhookReceiptStatus.Processed);
        recovered.Attempts.Should().Be(2);
        recovered.ProcessingStartedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task TwoDeliveriesOfTheSameEvent_ProcessItOnce()
    {
        // The real race: two requests in flight together, each with its own context and tracker.
        // The claim is one conditional UPDATE, so the database picks exactly one; the other is
        // refused as in flight or as already acted on, depending on how far the winner has got.
        var order = BuildOrder("INV-RACE", OrderStatus.PendingPayment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        await SeedPaymentAsync(order);

        await using var secondContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(_context.Database.GetConnectionString())
                .Options, TestQrCipher.Instance);

        var secondHandler = new ProcessMonobankWebhookCommandHandler(
            secondContext,
            NullLogger<ProcessMonobankWebhookCommandHandler>.Instance,
            _backgroundJobClientMock.Object,
            new Mock<RefundStatusSyncService>(null!, null!, null!).Object,
            new FuelFlow.SharedKernel.Observability.FuelFlowMetrics(),
            FuelFlow.SharedKernel.Observability.NotificationDispatcher.Disabled,
            TestJournal.For(secondContext));

        var command = WebhookCommand("INV-RACE", "success");

        var responses = await Task.WhenAll(
            _handler.HandleAsync(command),
            secondHandler.HandleAsync(command));

        responses.Count(r => r.NewStatus == OrderStatus.PendingFulfillment.ToString()).Should().Be(1,
            "exactly one delivery owns the process, however close together they arrive");

        var updated = await _context.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        updated.Status.Should().Be(OrderStatus.PendingFulfillment);

        (await _context.OutboxEvents.CountAsync()).Should().Be(1);
        (await _context.JournalEntries.CountAsync(e => e.SourceId == order.Id)).Should().Be(2);
        (await _context.Payments.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentStatus.Succeeded);
        (await _context.WebhookEvents.AsNoTracking().SingleAsync()).Attempts.Should().Be(2);
        _backgroundJobClientMock.Verify(
            x => x.Create(It.IsAny<Job>(), It.IsAny<IState>()),
            Times.Once);
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>
    /// Puts a delivery on file with a fresh lease and no processing behind it - the state a request
    /// leaves if its process dies between storing the delivery and acting on it.
    /// </summary>
    private async Task<WebhookEvent> RecordArrivalAsync(ProcessMonobankWebhookCommand command)
    {
        var recorded = await WebhookEventRecorder.RecordAsync(
            _context, command, NullLogger.Instance, CancellationToken.None);

        recorded.Decision.Should().Be(WebhookRecordDecision.Process);
        return recorded.Stored!;
    }

    /// <summary>
    /// The capture the checkout wrote for the invoice: the row the webhook moves to "succeeded".
    /// </summary>
    private async Task SeedPaymentAsync(Order order)
    {
        _context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = order.MonobankInvoiceId,
            AmountUah = order.Price,
            AmountKopecks = Money.ToKopecks(order.Price),
            Currency = "UAH",
            Status = PaymentStatus.Pending,
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Closes the accounting period the receipt would be posted into. A closed month is finished:
    /// nothing may be added to it, and that is how the ledger refuses a posting.
    /// </summary>
    private void CloseTheAccountingPeriod(Guid closedByUserId)
    {
        var today = DateOnly.FromDateTime(SystemClock.UtcNow);
        var start = new DateOnly(today.Year, today.Month, 1);

        _context.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = Guid.CreateVersion7(),
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            Status = PeriodStatus.Closed,
            ClosedAtUtc = TestClock.UtcNow,
            ClosedByUserId = closedByUserId
        });

        _context.SaveChanges();
    }
}
