using FluentAssertions;
using FuelFlow.Features.Acquiring;
using FuelFlow.Features.Acquiring.UnappliedReceipts;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.GetTrialBalance;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Payments.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Options;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The close check (#780): money the acquirer took with no order is bookable, and the trial balance
/// proves that the bank account and the acquirer's statement agree.
/// </summary>
/// <remarks>
/// Against a real PostgreSQL, because the posting engine's guarantees are the database's: the entry
/// number comes from an upsert, the journal refuses a closed period and an unbalanced entry through
/// triggers, and idempotency is a unique index rather than a read-then-write check.
/// </remarks>
[Collection("Integration Tests")]
public sealed class LedgerCloseCheckTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);
    private static readonly DateTime InPeriod = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;
    private readonly BookUnappliedReceiptsCommandHandler _book;
    private readonly GetTrialBalanceQueryHandler _report;
    private Guid _operatorId;

    public LedgerCloseCheckTests(TestDatabaseFixture fixture)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _journal = TestJournal.For(_context);

        var logger = NullLogger<BookUnappliedReceiptsCommandHandler>.Instance;
        _book = new BookUnappliedReceiptsCommandHandler(_context, _journal, logger);
        _report = new GetTrialBalanceQueryHandler(_context);

        TestJournal.SeedChart(_context);
    }

    private static int _userSeq;
    private static int _statementSeq;

    /// <summary>The class shares one cloned database, so every test starts from no money at all.</summary>
    public async Task InitializeAsync()
    {
        await PaymentTestReset.RunAsync(_context);

        // Not in the shared reset: statement rows are evidence another suite seeds deliberately, and
        // clearing them from a helper every class calls would be a side effect nobody asked for.
        await _context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE monobank_statement_entries CASCADE;");

        // The journal's posted-by column is a foreign key to a real user; an operator who books money
        // has to exist in the same database the posting names. Seeded after the reset, because the
        // settlement code is unique and a row left by the previous test would collide with it.
        var user = new FuelFlow.SharedKernel.Domain.User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = $"+38067{Interlocked.Increment(ref _userSeq):D7}",
            CreatedAtUtc = InPeriod,
            UpdatedAtUtc = InPeriod,
            SettlementCode = $"op{_userSeq}",
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        _operatorId = user.Id;
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    private void SeedStatement(
        string invoiceId,
        decimal amountKopecks,
        MonobankMerchant merchant = MonobankMerchant.Live,
        string status = "success",
        DateTime? paidAtUtc = null)
    {
        var now = paidAtUtc ?? InPeriod;

        _context.MonobankStatementEntries.Add(new MonobankStatementEntry
        {
            Id = Guid.CreateVersion7(),
            Merchant = merchant,
            StatementId = $"STMT-{Interlocked.Increment(ref _statementSeq)}-{invoiceId}",
            InvoiceId = invoiceId,
            AmountKopecks = amountKopecks,
            Ccy = 980,
            Status = status,
            AcquirerCreatedAtUtc = now,
            FirstSeenAtUtc = now,
            LastSeenAtUtc = now,
        });
        _context.SaveChanges();
    }

    private BookUnappliedReceiptsCommand Command() => new(From, To, _operatorId);

    // ------------------------------------------------------------- booking an unapplied receipt

    [Fact]
    public async Task A_Live_Capture_With_No_Order_Is_Booked_As_An_Advance()
    {
        SeedStatement("INV-UNBOOKED", 100m);

        var result = await _book.HandleAsync(Command());

        result.Booked.Should().ContainSingle();

        var payment = await _context.Payments.SingleAsync(p => p.ProviderInvoiceId == "INV-UNBOOKED");
        payment.OrderId.Should().BeNull("the money is real and belongs to no sale");
        payment.UserId.Should().BeNull("nobody is known to have paid it");
        payment.Merchant.Should().Be(MonobankMerchant.Live);
        payment.Status.Should().Be(PaymentStatus.Succeeded);
        payment.AmountUah.Should().Be(1.00m);
        payment.PaidAtUtc.Should().Be(InPeriod, "the statement's operation time is the authority");

        var entry = await _context.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .SingleAsync(e => e.SourceId == payment.Id);

        entry.SourceEvent.Should().Be("payment.received.unapplied");
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedByUserId.Should().Be(_operatorId, "a person stands behind money nobody ordered");
        entry.Lines.Single(l => l.DebitUah > 0).Account!.Code.Should().Be("311");
        entry.Lines.Single(l => l.CreditUah > 0).Account!.Code.Should().Be("681");
        entry.Lines.Sum(l => l.DebitUah).Should().Be(1.00m);
    }

    [Fact]
    public async Task Booking_The_Same_Period_Twice_Books_Nothing_The_Second_Time()
    {
        SeedStatement("INV-IDEMPOTENT", 100m);

        await _book.HandleAsync(Command());
        var second = await _book.HandleAsync(Command());

        second.CandidateCount.Should().Be(0);
        (await _context.Payments.CountAsync(p => p.ProviderInvoiceId == "INV-IDEMPOTENT")).Should().Be(1);
        (await _context.JournalEntries.CountAsync(e => e.SourceEvent == "payment.received.unapplied"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Sandbox_Money_Is_Never_Booked()
    {
        SeedStatement("INV-SANDBOX", 100m, merchant: MonobankMerchant.Sandbox);

        var result = await _book.HandleAsync(Command());

        result.CandidateCount.Should().Be(0, "the sandbox merchant simulates payments; no money moved");
        (await _context.Payments.AnyAsync(p => p.ProviderInvoiceId == "INV-SANDBOX")).Should().BeFalse();
    }

    [Fact]
    public async Task A_Capture_Payment_That_Already_Exists_Is_Not_Booked_As_Unapplied()
    {
        SeedStatement("INV-OWNED", 100m);

        // A capture row exists as soon as a checkout issued the invoice - even while it is still
        // Pending. Its settlement belongs to the webhook, which knows the order; booking it here
        // would put the same charge in the books twice once that callback arrives.
        _context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = "INV-OWNED",
            AmountUah = 1.00m,
            AmountKopecks = 100m,
            Status = PaymentStatus.Pending,
            CreatedAtUtc = InPeriod,
            UpdatedAtUtc = InPeriod,
        });
        await _context.SaveChangesAsync();

        var result = await _book.HandleAsync(Command());

        result.CandidateCount.Should().Be(0);
    }

    // ---------------------------------------------------------------- the close check itself

    [Fact]
    public async Task The_Bank_Is_Two_Hryvnia_Above_The_Books_Until_The_Unbooked_Captures_Are_Booked()
    {
        // October's shape in miniature: 13 792 through orders, 2.00 with no order.
        await _journal.PostAsync(new PostJournalEntryRequest
        {
            SourceEvent = "payment.received",
            SourceType = JournalSourceType.Payment,
            SourceId = Guid.NewGuid(),
            EntryDate = From,
            AmountUah = 13_792m,
            Description = "order payment",
            PostedByUserId = _operatorId,
        });

        // The checkout wrote a capture row when it issued the invoice; that is what makes this
        // invoice "known" and keeps it out of the unbooked list.
        _context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            Kind = PaymentKind.Capture,
            Provider = WebhookEventIdentity.Monobank,
            ProviderInvoiceId = "INV-ORDER-1",
            AmountUah = 13_792m,
            AmountKopecks = 1_379_200m,
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = InPeriod,
            CreatedAtUtc = InPeriod,
            UpdatedAtUtc = InPeriod,
        });
        await _context.SaveChangesAsync();

        SeedStatement("INV-ORDER-1", 1_379_200m);
        SeedStatement("INV-STRAY-1", 100m);
        SeedStatement("INV-STRAY-2", 100m);

        var before = await _report.HandleAsync(new TrialBalanceQuery { FromDate = From, ToDate = To });

        before.Totals.Balanced.Should().BeTrue("every entry posts as a balanced pair");
        before.Bank.StatementReceivedUah.Should().Be(13_794m);
        before.Bank.Ledger311DebitUah.Should().Be(13_792m);
        before.Bank.DifferenceUah.Should().Be(2m, "the bank holds money the books do not");
        before.Bank.Matches.Should().BeFalse();
        before.UnbookedCaptures.Should().HaveCount(2);
        before.ReadyToClose.Should().BeFalse("a balanced book can still be missing the bank's money");

        var closed = await _report.HandleAsync(new TrialBalanceQuery { FromDate = From, ToDate = To });
        closed.UnbookedCaptures.Select(c => c.InvoiceId).Should().BeEquivalentTo(["INV-STRAY-1", "INV-STRAY-2"]);

        await _book.HandleAsync(Command());

        var after = await _report.HandleAsync(new TrialBalanceQuery { FromDate = From, ToDate = To });

        after.Bank.Ledger311DebitUah.Should().Be(13_794m);
        after.Bank.DifferenceUah.Should().Be(0m);
        after.Bank.Matches.Should().BeTrue();
        after.UnbookedCaptures.Should().BeEmpty();
        after.ReadyToClose.Should().BeTrue();

        var advance = after.Accounts.Single(a => a.Code == "681");
        advance.ClosingSide.Should().Be(NormalBalance.Credit);
        advance.ClosingBalance.Should().Be(13_794m, "every hryvnia collected is still an advance");
    }

    [Fact]
    public async Task Drafts_Are_Reported_Apart_From_The_Books()
    {
        await _journal.PostProvisionalAsync(new PostJournalEntryRequest
        {
            SourceEvent = "voucher.redeemed.cogs",
            SourceType = JournalSourceType.Fulfillment,
            SourceId = Guid.NewGuid(),
            EntryDate = From,
            AmountUah = 45m,
            Description = "cost of a redemption nobody has confirmed",
            PostedByUserId = _operatorId,
        });

        var report = await _report.HandleAsync(new TrialBalanceQuery { FromDate = From, ToDate = To });

        // Both legs of the draft, because the report reads accounts and a posting has two sides.
        report.Drafts.Should().Contain(a => a.Code == "808" && a.Debit == 45m);
        report.Drafts.Should().Contain(a => a.Code == "281" && a.Credit == 45m);
        report.Accounts.Should().NotContain(a => a.Code == "808",
            "a draft is a claim nobody has confirmed, and the books do not carry it");
        report.Accounts.Should().NotContain(a => a.Code == "281");
        report.ReadyToClose.Should().BeFalse("an unconfirmed draft is not a closed month");
    }
}
