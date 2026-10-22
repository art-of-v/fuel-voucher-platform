using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// C-14: the ledger's keys are time-ordered, and the rows that were already there are brought
/// onto the same convention.
/// <para>
/// The application writes <c>Guid.CreateVersion7()</c> for the three accounting tables since this
/// change. The rows that existed before it carry random keys, and a random key on the table that
/// grows forever is the thing the change is about: every insert lands on a random leaf of the
/// B-tree. So the migration rewrites the keys of the rows that are there.
/// </para>
/// <para>
/// Rewriting a key is allowed - a key says nothing about the row it names - but only if the
/// accounting history survives it. That is what this class asserts, and it asserts it against the
/// MIGRATED data rather than against a description of it: the entries are written the way the old
/// code wrote them (random keys, a posted entry with frozen lines), the migration runs over them,
/// and then the rows are read back.
/// </para>
/// <para>
/// The shape of the test is the shape of a deployment: roll the schema back to the migration
/// before this one, write the rows the old code would have written, roll forward. The schema this
/// migration leaves behind is the schema it found - it changes data, not shape - so the rows are
/// written against the same tables either way.
/// </para>
/// <para>
/// <strong>The rows of one test stay in the database for the next one</strong>, because a posted
/// entry's lines cannot be deleted - that is the rule the journal is built on, and a cleanup would
/// be refused by the very trigger it was cleaning up after. So every test writes into a month of
/// its own and describes its entries in its own words, and every assertion is scoped to them.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class LedgerKeysAreTimeOrderedTests : IClassFixture<TestDatabaseFixture>
{
    /// <summary>The migration under test. The rollback target is the one before it.</summary>
    private const string TheMigration = "20261022113114_TheLedgerKeysAreTimeOrdered";

    /// <summary>
    /// Counts up over the tests in this class: every test gets its own month, its own account
    /// codes and its own wording, because the rows of one test are still there when the next
    /// one runs.
    /// </summary>
    private static int _sequence;

    private readonly TestDatabaseFixture _fixture;

    public LedgerKeysAreTimeOrderedTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// What the old code wrote: random keys, a period, a posted entry with two balanced lines, a
    /// draft with two more, and the sequence that numbers the entries in that period.
    /// </summary>
    private sealed record Written(
        Guid PeriodId,
        Guid EntryId,
        Guid DraftId,
        string PostedDescription,
        string DraftDescription,
        string PeriodStart,
        string SequenceName,
        string TamperHash);

    // -------------------------------------------------------------------------------------
    // The rewrite keeps the history.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_Entry_Keeps_Its_Lines_Its_Period_And_Its_Balance()
    {
        await WithWrittenLedgerAsync(async (written, context) =>
        {
            // The rows are all still there.
            (await CountAsync(context, $"SELECT count(*) FROM journal_entries WHERE description = '{written.PostedDescription}'"))
                .Should().Be(1);
            (await CountAsync(context, $"SELECT count(*) FROM journal_entries WHERE description = '{written.DraftDescription}'"))
                .Should().Be(1);
            (await CountAsync(context, $"SELECT count(*) FROM journal_lines l JOIN journal_entries e ON e.id = l.entry_id WHERE e.description IN ('{written.PostedDescription}', '{written.DraftDescription}')"))
                .Should().Be(4);

            // Every line still hangs off an entry, and every entry off a period. This is the
            // foreign key, and it is asked as a JOIN rather than as a constraint check because
            // the constraint is trivially satisfied by the migration re-adding it.
            (await CountAsync(context,
                $"SELECT count(*) FROM journal_lines l JOIN journal_entries e ON e.id = l.entry_id WHERE e.description IN ('{written.PostedDescription}', '{written.DraftDescription}')"))
                .Should().Be(4);
            (await CountAsync(context,
                $"SELECT count(*) FROM journal_entries e JOIN accounting_periods p ON p.id = e.period_id WHERE e.description IN ('{written.PostedDescription}', '{written.DraftDescription}')"))
                .Should().Be(2);

            // The posted entry still balances: the migration moved the money's columns around,
            // and a balance that came out wrong would be the worst thing it could do.
            //
            // The entry is found by what it says rather than by what it is called: its key is
            // what the migration changed, so a balance read by the old key would find nothing
            // and balance nothing. The line count comes first, so the balance cannot be zero
            // because there was nothing to add up.
            (await CountAsync(context,
                $"SELECT count(*) FROM journal_lines l JOIN journal_entries e ON e.id = l.entry_id WHERE e.description = '{written.PostedDescription}'"))
                .Should().Be(2);
            (await BalanceOfAsync(context, written.PostedDescription)).Should().Be(0m);

            // And the draft still balances too, because it did.
            (await CountAsync(context,
                $"SELECT count(*) FROM journal_lines l JOIN journal_entries e ON e.id = l.entry_id WHERE e.description = '{written.DraftDescription}'"))
                .Should().Be(2);
            (await BalanceOfAsync(context, written.DraftDescription)).Should().Be(0m);
        });
    }

    [Fact]
    public async Task An_Entry_Keeps_Every_Thing_That_Is_Not_Its_Key()
    {
        await WithWrittenLedgerAsync(async (written, context) =>
        {
            // The entry is found by what it says, not by what it is called: the key is the one
            // thing the migration is allowed to change.
            var entry = await context.Database
                .SqlQueryRaw<string>(
                    $"""
                    SELECT entry_no || '|' || description || '|' || status || '|' || COALESCE(tamper_hash, '')
                    FROM journal_entries
                    WHERE description = '{written.PostedDescription}'
                    """)
                .ToListAsync();

            entry.Should().ContainSingle();
            entry[0].Should().Be($"1|{written.PostedDescription}|Posted|{written.TamperHash}",
                "the number, the description, the status and the tamper hash are the entry's own "
                + "content, and the tamper hash is a checksum over that content - not over the key");

            // The amounts are the money, and they are on the lines.
            var amounts = await context.Database
                .SqlQueryRaw<string>(
                    $"""
                    SELECT string_agg(l.debit_uah::text, ',' ORDER BY l.line_no) || '|' ||
                           string_agg(l.credit_uah::text, ',' ORDER BY l.line_no)
                    FROM journal_lines l
                    JOIN journal_entries e ON e.id = l.entry_id
                    WHERE e.description = '{written.PostedDescription}'
                    """)
                .ToListAsync();

            amounts.Should().ContainSingle();
            amounts[0].Should().Be("100.00,0.00|0.00,100.00");
        });
    }

    [Fact]
    public async Task The_Old_Keys_Are_Gone_And_The_New_Ones_Are_Time_Ordered()
    {
        await WithWrittenLedgerAsync(async (written, context) =>
        {
            // The old keys address nothing any more.
            (await CountAsync(context, $"SELECT count(*) FROM journal_entries WHERE id = '{written.EntryId}'"))
                .Should().Be(0);
            (await CountAsync(context, $"SELECT count(*) FROM accounting_periods WHERE id = '{written.PeriodId}'"))
                .Should().Be(0);

            // Every key in the three tables is a version 7 uuid, which is the convention the
            // rest of the schema uses. The version is the 13th hex digit of the key - the first
            // digit of the third group - and the variant is the 17th, which is '8' or '9'.
            //
            // The database has no function for either: uuid_extract_version() and uuidv7() are
            // PostgreSQL 18, and this platform runs 16.
            (await CountAsync(context, "SELECT count(*) FROM journal_entries WHERE substring(id::text, 15, 1) <> '7'"))
                .Should().Be(0);
            (await CountAsync(context, "SELECT count(*) FROM journal_lines WHERE substring(id::text, 15, 1) <> '7'"))
                .Should().Be(0);
            (await CountAsync(context, "SELECT count(*) FROM accounting_periods WHERE substring(id::text, 15, 1) <> '7'"))
                .Should().Be(0);

            // The variant bits are the other half of "this is a version 7 key" rather than a
            // version 7 key with the wrong shape.
            (await CountAsync(context, "SELECT count(*) FROM journal_entries WHERE substring(id::text, 20, 1) NOT IN ('8', '9')"))
                .Should().Be(0);
            (await CountAsync(context, "SELECT count(*) FROM journal_lines WHERE substring(id::text, 20, 1) NOT IN ('8', '9')"))
                .Should().Be(0);
            (await CountAsync(context, "SELECT count(*) FROM accounting_periods WHERE substring(id::text, 20, 1) NOT IN ('8', '9')"))
                .Should().Be(0);

            // And they sort by the moment the row was written. That is the whole point of the
            // change: the index is written at its right-hand edge instead of across the whole
            // key space, and that only holds if the key carries the time.
            (await CountAsync(context,
                """
                SELECT count(*) FROM (
                    SELECT row_number() OVER (ORDER BY id) AS by_key,
                           row_number() OVER (ORDER BY created_at_utc) AS by_time
                    FROM journal_entries
                ) t
                WHERE by_key <> by_time
                """))
                .Should().Be(0);
        });
    }

    [Fact]
    public async Task The_Sequence_That_Numbers_The_Entries_Follows_The_Period()
    {
        await WithWrittenLedgerAsync(async (written, context) =>
        {
            // The sequence is NAMED after the period - "journal:<period id>" - and the period's
            // key just changed. If the name did not follow, the next posting into that period
            // would start counting at 1 again and collide with the entry number 1 the period
            // already holds.
            (await CountAsync(context,
                $"SELECT count(*) FROM document_sequences WHERE name = '{written.SequenceName}'"))
                .Should().Be(0, "the name still points at the period's old key");

            (await CountAsync(context,
                $"SELECT count(*) FROM document_sequences s JOIN accounting_periods p ON s.name = 'journal:' || p.id::text WHERE p.period_start = '{written.PeriodStart}'"))
                .Should().Be(1, "the name points at the period's new key");
        });
    }

    // -------------------------------------------------------------------------------------
    // The machinery.
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Rolls the schema back to the migration before this one, writes the rows the old code
    /// would have written, rolls forward over them, and then runs the assertions.
    /// </summary>
    private async Task WithWrittenLedgerAsync(Func<Written, ApplicationDbContext, Task> body)
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(MigrationBefore(context, TheMigration));
            var written = await WriteAsync(context);
            await migrator.MigrateAsync();
            await body(written, context);
        }
        finally
        {
            // The schema goes back to head whatever happened, so one failure cannot cascade
            // into the next test.
            await migrator.MigrateAsync();
        }
    }

    /// <summary>
    /// The migration immediately before the one under test. Read from the assembly rather than
    /// written down, so a migration added between the two does not turn the rollback into a
    /// walk over somebody else's work.
    /// </summary>
    private static string MigrationBefore(ApplicationDbContext context, string migration)
    {
        var migrations = context.GetService<IMigrationsAssembly>().Migrations.Keys
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var index = migrations.IndexOf(migration);
        index.Should().BeGreaterThan(0, "the migration under test has to be in the assembly, with one before it");

        return migrations[index - 1];
    }

    /// <summary>
    /// Writes the ledger the way the old code wrote it: random keys, a period, a posted entry
    /// with two balanced lines, a draft with two more, and the sequence that numbers the
    /// entries in that period.
    /// </summary>
    private static async Task<Written> WriteAsync(ApplicationDbContext context)
    {
        var sequence = Interlocked.Increment(ref _sequence);
        var periodId = Guid.NewGuid();
        var cashId = Guid.NewGuid();
        var revenueId = Guid.NewGuid();
        var posterId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var tamperHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        // A month of its own for this test: the period's start is unique in the schema, and
        // the rows of the previous tests are still here.
        var start = new DateOnly(2031, 1, 1).AddMonths(sequence);
        var end = start.AddMonths(1).AddDays(-1);
        var postedDate = start.AddDays(14);
        var draftDate = start.AddDays(15);
        var postedDescription = $"the posted entry {sequence}";
        var draftDescription = $"the draft entry {sequence}";

        // A poster, because a posted entry has to name one.
        var phone = "+3805" + Guid.NewGuid().ToString("N")[..8];
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc)
            VALUES ({0}, {1}, now(), now())
            """,
            posterId,
            phone);

        // Two accounts: one to debit, one to credit. The code is unique in the chart, and the
        // chart of the previous tests is still here.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active)
            VALUES ({0}, {1}, 'Cash', 'Asset', 'Debit', true, true),
                   ({2}, {3}, 'Revenue', 'Revenue', 'Credit', true, true)
            """,
            cashId,
            $"91{sequence:00}",
            revenueId,
            $"92{sequence:00}");

        // The period. Its dates are inside the month the entries are dated in.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO accounting_periods (id, period_start, period_end, status)
            VALUES ({0}, {1}::date, {2}::date, 'Open')
            """,
            periodId,
            $"{start:yyyy-MM-dd}",
            $"{end:yyyy-MM-dd}");

        // The posted entry: a draft first, its lines, and then the flip - which is the order
        // the application posts in, and the order the frozen-lines trigger insists on.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO journal_entries (id, entry_no, entry_date, period_id, source_type, source_event,
                                         source_id, description, status, posted_at_utc, posted_by_user_id,
                                         created_at_utc, tamper_hash)
            VALUES ({0}, 1, {1}::date, {2}, 'Adjustment', 'adjustment.ledgerkeys', {3},
                    {4}, 'Draft', NULL, NULL, {5}::timestamptz, {6})
            """,
            entryId,
            $"{postedDate:yyyy-MM-dd}",
            periodId,
            Guid.NewGuid(),
            postedDescription,
            $"{postedDate:yyyy-MM-dd} 08:00:00+00",
            tamperHash);

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah)
            VALUES ({0}, {1}, 1, {2}, 100.00, 0),
                   ({3}, {1}, 2, {4}, 0, 100.00)
            """,
            Guid.NewGuid(),
            entryId,
            cashId,
            Guid.NewGuid(),
            revenueId);

        await context.Database.ExecuteSqlRawAsync(
            """
            UPDATE journal_entries
               SET status = 'Posted', posted_at_utc = now(), posted_by_user_id = {0}
             WHERE id = {1}
            """,
            posterId,
            entryId);

        // A second entry, written a day later. Two entries with different timestamps are what
        // makes "the keys sort by the moment the row was written" say something.
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO journal_entries (id, entry_no, entry_date, period_id, source_type, source_event,
                                         source_id, description, status, posted_at_utc, posted_by_user_id,
                                         created_at_utc)
            VALUES ({0}, 2, {1}::date, {2}, 'Adjustment', 'adjustment.ledgerkeys', {3},
                    {4}, 'Draft', NULL, NULL, {5}::timestamptz)
            """,
            draftId,
            $"{draftDate:yyyy-MM-dd}",
            periodId,
            Guid.NewGuid(),
            draftDescription,
            $"{draftDate:yyyy-MM-dd} 08:00:00+00");

        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah)
            VALUES ({0}, {1}, 1, {2}, 50.00, 0),
                   ({3}, {1}, 2, {4}, 0, 50.00)
            """,
            Guid.NewGuid(),
            draftId,
            cashId,
            Guid.NewGuid(),
            revenueId);

        // The sequence that hands out the entry numbers, named after the period.
        var sequenceName = $"journal:{periodId}";
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO document_sequences (id, name, last_value)
            VALUES ({0}, {1}, 2)
            """,
            Guid.NewGuid(),
            sequenceName);

        return new Written(
            periodId,
            entryId,
            draftId,
            postedDescription,
            draftDescription,
            $"{start:yyyy-MM-dd}",
            sequenceName,
            tamperHash);
    }

    /// <summary>
    /// A single count, as a number. <c>ExecuteSqlRawAsync</c> would return rows-affected (-1 for
    /// a SELECT), which compares equal to nothing and passes against any row count at all.
    /// </summary>
    private static async Task<int> CountAsync(ApplicationDbContext context, string sql)
    {
        var result = await context.Database.SqlQueryRaw<int>(sql).ToListAsync();
        return result.Single();
    }

    /// <summary>
    /// What an entry's lines add up to: the debits minus the credits. Zero is a balanced entry.
    /// The entry is named by what it says, because its key is what the migration changed.
    /// </summary>
    private static async Task<decimal> BalanceOfAsync(ApplicationDbContext context, string description)
    {
        var result = await context.Database
            .SqlQueryRaw<decimal>(
                """
                SELECT COALESCE(SUM(l.debit_uah), 0) - COALESCE(SUM(l.credit_uah), 0)
                FROM journal_lines l
                JOIN journal_entries e ON e.id = l.entry_id
                WHERE e.description = {0}
                """,
                description)
            .ToListAsync();

        return result.Single();
    }
}
