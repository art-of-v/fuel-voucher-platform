using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// What <c>TheVatShareGetsItsOwnAccount</c> does to the redemptions that were <b>already drafted</b>
/// when it runs. The schema after the migration is checked elsewhere; this class checks the rows.
/// </summary>
/// <remarks>
/// <para>
/// The previous build composed a redemption out of two drafts: revenue net of VAT, and a VAT pair
/// that moved the share from 643 back to 703. Both are drafts - nobody confirmed the supplier's
/// evidence - so they are not in the books, and the migration corrects them rather than reversing
/// them. It folds the VAT draft into the revenue draft's legs and drops the VAT draft, because after
/// the change the advance is released gross and the VAT was already carved out at the receipt.
/// </para>
/// <para>
/// That fold is the whole reason this test rolls the schema back: a draft seeded at head already has
/// the new shape, so there is nothing for the migration to convert. The sequence here is production's
/// own - rows written by the old build, then the migration - and the <c>finally</c> returns the schema
/// to head so a failure cannot cascade into the next test.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class VatCarveOutBackfillTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>The schema the old build wrote against: the migration immediately before this one.</summary>
    private const string BeforeVatAccount = "20261025070103_TheSignedOutDeviceKeepsTheSignInCode";

    private const decimal Gross = 2500m;
    private const decimal VatShare = 416.67m;
    private const decimal Net = Gross - VatShare;

    private readonly TestDatabaseFixture _fixture;

    public VatCarveOutBackfillTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task An_Unconfirmed_Redemption_Becomes_One_Gross_Revenue_Draft()
    {
        var voucherId = Guid.NewGuid();

        await WithSchemaFrom(BeforeVatAccount, async context =>
        {
            var periodId = await EnsureOctoberPeriodAsync(context);

            // Exactly what RedemptionJournalPoster used to write for a 2500 line at 20%.
            await InsertDraftAsync(
                context, periodId, voucherId,
                sourceEvent: "voucher.redeemed.revenue",
                debitCode: "681", creditCode: "702", amount: Net);
            await InsertDraftAsync(
                context, periodId, voucherId,
                sourceEvent: "voucher.redeemed.vat",
                debitCode: "643", creditCode: "703", amount: VatShare);
        });

        await using var verify = CreateContext();

        var revenue = await verify.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .SingleAsync(e => e.SourceId == voucherId && e.SourceEvent == "voucher.redeemed.revenue");

        revenue.Status.Should().Be(JournalEntryStatus.Draft,
            "the draft is still unconfirmed; the migration corrects it, it does not post it");
        revenue.Lines.Single(l => l.DebitUah > 0).Account!.Code.Should().Be("681");
        revenue.Lines.Single(l => l.DebitUah > 0).DebitUah.Should().Be(Gross,
            "the advance is released gross, so the debit and the credit both grow by the VAT share");
        revenue.Lines.Single(l => l.CreditUah > 0).Account!.Code.Should().Be("702");
        revenue.Lines.Single(l => l.CreditUah > 0).CreditUah.Should().Be(Gross);
        revenue.Lines.Should().HaveCount(2, "the VAT draft's two legs fold into these, they do not join them");

        (await verify.JournalEntries
                .AnyAsync(e => e.SourceId == voucherId && e.SourceEvent == "voucher.redeemed.vat"))
            .Should().BeFalse("the VAT is carved out at the receipt now and must not be carved again here");

        (await verify.LedgerMappingRules.AnyAsync(r => r.SourceEvent == "voucher.redeemed.vat"))
            .Should().BeFalse("a retired event with a mapping would keep posting the second share");

        var receiptVat = await verify.LedgerMappingRules
            .Include(r => r.Account)
            .Where(r => r.SourceEvent == "payment.received.vat")
            .ToListAsync();
        receiptVat.Single(r => r.Side == NormalBalance.Debit).Account!.Code.Should().Be("704",
            "the deduction account is 704 under the plan, and 703 is service revenue");
        receiptVat.Single(r => r.Side == NormalBalance.Credit).Account!.Code.Should().Be("643");

        var renamed = await verify.ChartOfAccounts.SingleAsync(a => a.Code == "703");
        renamed.NameUkr.Should().Be("Дохід від реалізації робіт і послуг");
        renamed.NormalBalance.Should().Be(NormalBalance.Credit);

        (await verify.ChartOfAccounts.AnyAsync(a => a.Code == "704")).Should().BeTrue();

        var unapplied = await verify.LedgerMappingRules
            .Include(r => r.Account)
            .Where(r => r.SourceEvent == "payment.received.unapplied")
            .ToListAsync();
        unapplied.Single(r => r.Side == NormalBalance.Debit).Account!.Code.Should().Be("311");
        unapplied.Single(r => r.Side == NormalBalance.Credit).Account!.Code.Should().Be("681");
    }

    [Fact]
    public async Task A_Redemption_Already_Posted_Is_History_And_Is_Left_Alone()
    {
        // The fold filters on status = 'Draft' in every statement. Without that filter it would
        // rewrite an entry an auditor has already read - and the ledger's own triggers would not
        // stop it, because the migration removes the 703 lines before anyone looks at the balance.
        var voucherId = Guid.NewGuid();

        await WithSchemaFrom(BeforeVatAccount, async context =>
        {
            var periodId = await EnsureOctoberPeriodAsync(context);

            var user = new User
            {
                Id = Guid.NewGuid(),
                PhoneNumber = $"+38099{voucherId:N}"[..16],
                SettlementCode = $"bk{voucherId:N}"[..8],
                CreatedAtUtc = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc),
                UpdatedAtUtc = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc),
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var revenueId = await InsertDraftAsync(
                context, periodId, voucherId,
                sourceEvent: "voucher.redeemed.revenue",
                debitCode: "681", creditCode: "702", amount: Net);
            var vatId = await InsertDraftAsync(
                context, periodId, voucherId,
                sourceEvent: "voucher.redeemed.vat",
                debitCode: "643", creditCode: "703", amount: VatShare);

            await PostAsync(context, revenueId, user.Id);
            await PostAsync(context, vatId, user.Id);
        });

        await using var verify = CreateContext();

        var entries = await verify.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => e.SourceId == voucherId)
            .ToListAsync();

        entries.Should().HaveCount(2, "both were posted under the old model and both stay on the books");
        entries.Should().OnlyContain(e => e.Status == JournalEntryStatus.Posted);

        var revenue = entries.Single(e => e.SourceEvent == "voucher.redeemed.revenue");
        revenue.Lines.Sum(l => l.DebitUah).Should().Be(Net, "a posted entry is not re-cut to today's policy");

        var vat = entries.Single(e => e.SourceEvent == "voucher.redeemed.vat");
        vat.Lines.Single(l => l.DebitUah > 0).Account!.Code.Should().Be("643");
        vat.Lines.Single(l => l.CreditUah > 0).Account!.Code.Should().Be("703");
        vat.Lines.Sum(l => l.CreditUah).Should().Be(VatShare);
    }

    /// <summary>
    /// Runs <paramref name="body"/> against the schema as it stood before the VAT migration, then
    /// brings the database back to head whatever happens.
    /// </summary>
    private async Task WithSchemaFrom(string migration, Func<ApplicationDbContext, Task> body)
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(migration);
            await body(context);
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// The October period, whichever way it got there: the two tests share one database, and a
    /// period may also have been opened by whatever the previous test posted. Reusing it is the
    /// realistic shape anyway - a month is opened once.
    /// </summary>
    private static async Task<Guid> EnsureOctoberPeriodAsync(ApplicationDbContext context)
    {
#pragma warning disable EF1002 // identifiers and values, not SQL fragments; all from this file
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO accounting_periods (id, period_start, period_end, status)
            VALUES (gen_random_uuid(), '2026-10-01', '2026-10-31', 'Open')
            ON CONFLICT (period_start) DO NOTHING
            """);

        return (await context.Database
            .SqlQueryRaw<Guid>("""SELECT id AS "Value" FROM accounting_periods WHERE period_start = '2026-10-01'""")
            .ToListAsync()).Single();
#pragma warning restore EF1002
    }

    /// <summary>
    /// Writes one two-legged draft the way the previous build did: a debit to
    /// <paramref name="debitCode"/>, a credit to <paramref name="creditCode"/>, both for
    /// <paramref name="amount"/>.
    /// </summary>
    private static async Task<Guid> InsertDraftAsync(
        ApplicationDbContext context,
        Guid periodId,
        Guid sourceId,
        string sourceEvent,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        var entryId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

#pragma warning disable EF1002 // identifiers and values, not SQL fragments; all from this file
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO journal_entries
                (id, entry_no, entry_date, period_id, source_type, source_event, source_id,
                 description, status, created_at_utc)
            SELECT {0},
                   (SELECT COALESCE(MAX(entry_no), 0) + 1 FROM journal_entries WHERE period_id = {1}),
                   '2026-10-15', {1}, 'Fulfillment', {2}, {3}, {4}, 'Draft', {5};

            INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah)
            SELECT gen_random_uuid(), {0}, 1, a.id, {6}, 0
            FROM chart_of_accounts a WHERE a.code = {7};

            INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah)
            SELECT gen_random_uuid(), {0}, 2, a.id, 0, {6}
            FROM chart_of_accounts a WHERE a.code = {8};
            """,
            entryId, periodId, sourceEvent, sourceId, $"{sourceEvent} draft", now,
            amount, debitCode, creditCode);
#pragma warning restore EF1002

        return entryId;
    }

    /// <summary>
    /// Posts a draft: the lines go in while it is still a draft (a posted entry's lines are frozen),
    /// and the status flips afterwards, which is the moment the coherence and balance triggers judge it.
    /// </summary>
    private static async Task PostAsync(ApplicationDbContext context, Guid entryId, Guid userId)
    {
#pragma warning disable EF1002 // identifiers and values, not SQL fragments; all from this file
        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE journal_entries
            SET status = 'Posted', posted_at_utc = now(), posted_by_user_id = {0}
            WHERE id = {1}
            """, userId, entryId);
#pragma warning restore EF1002
    }
}
