using FluentAssertions;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.GetTrialBalance;
using FuelFlow.Features.Ledger.ManualEntries;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The two accounts whose balance means something only if the other side was posted (planning #787):
/// 643 «Податкові зобов'язання», settled into 641 «Розрахунки за податками» by a filed return, and
/// 281 «Товари на складі», which needs a buy side before it can be credited by a sale.
/// </summary>
/// <remarks>
/// <para>
/// The decisions these hold to were made on 2026-10-31: the VAT is settled by an operator entry when
/// the return is filed (Dr 643 / Cr 641) and paid from the bank (Dr 641 / Cr 311), and stock enters
/// the books through the opening balance (Dr 281 / Cr 441) and each supplier invoice
/// (Dr 281 / Cr 631). Both are manual entries - <c>POST /api/admin/ledger/entries</c> - because a
/// filed return and a supplier invoice are not caused by any row the platform writes.
/// </para>
/// <para>
/// Against a real PostgreSQL: idempotency is the journal's unique index, and the checks read every
/// line posted before the period opens, which no in-memory provider would model the same way.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class LedgerSettlementTests : IClassFixture<TestDatabaseFixture>, IAsyncLifetime
{
    private readonly ApplicationDbContext _context;
    private readonly JournalPostingService _journal;
    private Guid _operatorId;

    public LedgerSettlementTests(TestDatabaseFixture fixture)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options;

        _context = new ApplicationDbContext(options, TestQrCipher.Instance);
        _journal = TestJournal.For(_context);
    }

    private static int _monthSeq;
    private static int _userSeq;

    /// <summary>
    /// A month no other test writes to. The check reads every journal line before the period opens,
    /// so a month shared with another class would carry that class's VAT into these assertions.
    /// </summary>
    private static DateOnly NewMonth() => new DateOnly(2036, 1, 1).AddMonths(Interlocked.Increment(ref _monthSeq));

    private static DateOnly PeriodEnd(DateOnly month) => month.AddMonths(1).AddDays(-1);

    public async Task InitializeAsync()
    {
        await PaymentTestReset.RunAsync(_context);

        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = $"+38068{Interlocked.Increment(ref _userSeq):D7}",
            CreatedAtUtc = TestClock.UtcNow,
            UpdatedAtUtc = TestClock.UtcNow,
            SettlementCode = $"sts{_userSeq}",
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

    private static PostManualJournalEntryRequest Manual(
        DateOnly date,
        string reference,
        string description,
        params ManualJournalLineRequest[] lines)
        => new()
        {
            EntryDate = date,
            Reference = reference,
            Description = description,
            Lines = lines,
        };

    private static ManualJournalLineRequest Dr(string accountCode, decimal amount)
        => new() { AccountCode = accountCode, DebitUah = amount };

    private static ManualJournalLineRequest Cr(string accountCode, decimal amount)
        => new() { AccountCode = accountCode, CreditUah = amount };

    private static string NewReference() => $"settlement-{Guid.NewGuid():N}";

    private Task<JournalEntry> PostAsync(
        DateOnly date,
        string reference,
        string description,
        params ManualJournalLineRequest[] lines)
        => _journal.PostManualAsync(Manual(date, reference, description, lines), _operatorId);

    private Task<TrialBalanceResponse> ReportAsync(DateOnly month)
        => new GetTrialBalanceQueryHandler(_context).HandleAsync(
            new TrialBalanceQuery { FromDate = month, ToDate = PeriodEnd(month) });

    [Fact]
    public async Task The_chart_has_an_account_to_settle_the_vat_into()
    {
        var account = await _context.ChartOfAccounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Code == "641");

        account.Should().NotBeNull("the migration seeds 641 under the class-6 header");
        account!.IsPostable.Should().BeTrue("a return is settled into it, so it is not a grouping");
        account.IsActive.Should().BeTrue();
        account.AccountType.Should().Be(AccountType.Liability);
        account.NormalBalance.Should().Be(NormalBalance.Credit);
    }

    [Fact]
    public async Task The_vat_carried_into_a_period_must_be_settled()
    {
        var month = NewMonth();

        // The VAT of the previous month's payments, booked the way payment.received.vat books it.
        await PostAsync(
            month.AddMonths(-1),
            NewReference(),
            "VAT carved out of a receipt",
            Dr("704", 122m),
            Cr("643", 122m));

        var before = await ReportAsync(month);

        before.Vat.LiabilityCarriedInUah.Should().Be(122m);
        before.Vat.ChargedInPeriodUah.Should().Be(0m);
        before.Vat.SettledInPeriodUah.Should().Be(0m);
        before.Vat.OlderThanOnePeriodUah.Should().Be(122m, "a liability that entered the period and did not leave it");
        before.Vat.Settled.Should().BeFalse();
        before.ReadyToClose.Should().BeFalse(
            "VAT collected from customers and never declared is not a closed month");

        // The filed return: the liability moves from charged to payable.
        await PostAsync(
            month,
            NewReference(),
            "VAT return filed",
            Dr("643", 122m),
            Cr("641", 122m));

        var after = await ReportAsync(month);

        after.Vat.SettledInPeriodUah.Should().Be(122m);
        after.Vat.OlderThanOnePeriodUah.Should().Be(0m);
        after.Vat.LiabilityClosingUah.Should().Be(0m);
        after.Vat.PayableToBudgetUah.Should().Be(122m, "declared and not yet paid");
        after.Vat.Settled.Should().BeTrue();
        after.ReadyToClose.Should().BeTrue("nothing else is outstanding in this period");
    }

    [Fact]
    public async Task A_credit_balance_on_inventory_is_a_missing_buy_side()
    {
        var month = NewMonth();

        // Cost of a sale booked before anything was ever received onto the shelf - the #787 shape.
        await PostAsync(month, NewReference(), "Cost of a redeemed voucher", Dr("808", 500m), Cr("281", 500m));

        var before = await ReportAsync(month);

        before.Stock.ClosingSide.Should().Be(NormalBalance.Credit);
        before.Stock.MissingBuySideUah.Should().Be(500m);
        before.Stock.Complete.Should().BeFalse();
        before.ReadyToClose.Should().BeFalse("stock was sold before it was bought");

        // The supplier invoice that puts the stock on the shelf.
        await PostAsync(month, NewReference(), "Supplier invoice", Dr("281", 500m), Cr("631", 500m));

        var after = await ReportAsync(month);

        after.Stock.InventoryClosingUah.Should().Be(0m);
        after.Stock.MissingBuySideUah.Should().Be(0m);
        after.Stock.Complete.Should().BeTrue();
        after.ReadyToClose.Should().BeTrue();
    }

    [Fact]
    public async Task Each_settlement_posting_balances_and_replays_once()
    {
        var month = NewMonth();

        var vatReference = NewReference();
        var vat = await PostAsync(
            month, vatReference, "VAT return filed", Dr("643", 90m), Cr("641", 90m));
        var vatReplay = await PostAsync(
            month, vatReference, "VAT return filed (retry)", Dr("643", 90m), Cr("641", 90m));

        vatReplay.Id.Should().Be(vat.Id, "the reference is the idempotency key");
        vat.Lines.Sum(l => l.DebitUah).Should().Be(vat.Lines.Sum(l => l.CreditUah));
        (await _context.JournalEntries.CountAsync(e => e.SourceId == vat.SourceId))
            .Should().Be(1, "a retried entry must not post twice");

        var stockReference = NewReference();
        var purchase = await PostAsync(
            month, stockReference, "Supplier invoice", Dr("281", 700m), Cr("631", 700m));
        var purchaseReplay = await PostAsync(
            month, stockReference, "Supplier invoice (retry)", Dr("281", 700m), Cr("631", 700m));

        purchaseReplay.Id.Should().Be(purchase.Id);
        purchase.Lines.Sum(l => l.DebitUah).Should().Be(purchase.Lines.Sum(l => l.CreditUah));
        (await _context.JournalEntries.CountAsync(e => e.SourceId == purchase.SourceId)).Should().Be(1);

        var report = await ReportAsync(month);
        report.Vat.SettledInPeriodUah.Should().Be(90m);
        report.Stock.Complete.Should().BeTrue("the stock was received before anything credited it");
    }
}
