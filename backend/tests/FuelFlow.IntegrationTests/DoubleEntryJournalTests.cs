using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// D-03 (the journal's shape), D-04 (a posted entry's money is frozen) and D-05 (posted entries
/// balance).
/// <para>
/// These triggers are the difference between "a table with debit and credit columns" and double-entry
/// accounting, and they are the two objects this project has deferred twice. Each is asserted against
/// the live database rather than through a handler - the point is that the database refuses the
/// write, including from a <c>psql</c> session.
/// </para>
/// <para>
/// Nothing writes to the journal yet. These are guard rails standing on an empty track, which is the
/// The engine that writes here is <c>JournalPostingService</c>, covered by
/// <see cref="JournalPostingServiceTests"/>. These tests stay at the trigger level on purpose: the
/// point is that the database refuses the write, including from a <c>psql</c> session, which a test
/// that went through a service could not prove.
/// </para>
/// <para>
/// <strong>No per-test cleanup</strong>, because it is impossible rather than unnecessary: a posted
/// entry's lines cannot be deleted, which is the rule under test, so a cleanup would be refused by
/// the very trigger it is cleaning up after. It is also not needed - the suite gives this class its
/// own clone of the migrated schema and discards it afterwards. Every test therefore takes unique ids,
/// a unique account code and a unique accounting month.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class DoubleEntryJournalTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DoubleEntryJournalTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private static int _codeSeq = 2000;
    private static int _periodSeq;

    private sealed record Scaffold(
        Guid PeriodId, Guid CashId, Guid RevenueId, Guid PosterId, string Date, string NextMonthDate);

    /// <summary>
    /// Monotonic, because the chart's code is unique and every test in this class shares one
    /// database. A collision would be a failure that only shows up sometimes.
    /// </summary>
    private static string NewAccountCode() => Interlocked.Increment(ref _codeSeq).ToString();

    private async Task<Guid> SeedAccountAsync(string type, string side)
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active) VALUES ('{id}', '{NewAccountCode()}', 'Рахунок', '{type}', '{side}', true, true)");
        return id;
    }

    private async Task<Guid> SeedOperatorAsync()
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc) VALUES ('{id}', '+38050{suffix}', now(), now())");
        return id;
    }

    private static (DateOnly Start, DateOnly End) MonthRange(int year, int monthOffset)
    {
        var start = new DateOnly(year, 1, 1).AddMonths(monthOffset);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// Each test gets its own month, counted automatically.
    /// <para>
    /// Not tidiness: <c>period_start</c> is UNIQUE, because two periods cannot both begin on the same
    /// day - you would not know which month an entry dated inside it belongs to. Tests that all used
    /// March collided on it, so the month advances per call and no test can collide by accident.
    /// </para>
    /// </summary>
    private async Task<Scaffold> SeedAsync()
    {
        var cash = await SeedAccountAsync("Asset", "Debit");
        var revenue = await SeedAccountAsync("Revenue", "Credit");
        var poster = await SeedOperatorAsync();

        await using var context = CreateContext();
        var period = Guid.NewGuid();
        var (start, end) = MonthRange(2027, Interlocked.Increment(ref _periodSeq));
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO accounting_periods (id, period_start, period_end, status) VALUES ('{period}', '{start:yyyy-MM-dd}', '{end:yyyy-MM-dd}', 'Open')");

        return new Scaffold(period, cash, revenue, poster, $"{start:yyyy-MM-dd}", $"{end.AddMonths(1):yyyy-MM-dd}");
    }

    /// <summary>Runs a sequence and hands back what PostgreSQL said, rather than throwing.</summary>
    private static async Task<string?> ErrorOfAsync(Func<Task> body)
    {
        try { await body(); return null; }
        catch (PostgresException ex) { return ex.MessageText; }
    }

    private static string EntrySql(Scaffold s, Guid entry, string date, int? entry_no = null)
        => $"INSERT INTO journal_entries (id, entry_no, entry_date, period_id, source_type, source_event, source_id, description, status, posted_at_utc, posted_by_user_id, created_at_utc) VALUES ('{entry}', {entry_no ?? 1}, '{date}', '{s.PeriodId}', 'Adjustment', 'adjustment.probe', '{Guid.NewGuid()}', 'probe', 'Draft', NULL, NULL, now())";

    private static string LineSql(Guid entry, short lineNo, Guid account, decimal debit, decimal credit)
    {
        var d = debit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var c = credit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah) VALUES ('{Guid.NewGuid()}', '{entry}', {lineNo}, '{account}', {d}, {c})";
    }

    private static string FlipSql(Scaffold s, Guid entry)
        => $"UPDATE journal_entries SET status = 'Posted', posted_at_utc = now(), posted_by_user_id = '{s.PosterId}' WHERE id = '{entry}'";

    /// <summary>
    /// One statement per round trip, never a concatenated batch.
    /// <para>
    /// A posting really happens in this order - a Draft entry, its lines, then the flip - and sent as
    /// one batch it collapses into a single implicit transaction, where the lines land against an
    /// entry that already reads as posted. Application code saves a change at a time anyway, so this
    /// matches the production shape rather than papering over the difference.
    /// </para>
    /// </summary>
    private static async Task PostBalancedAsync(ApplicationDbContext ctx, Scaffold s, Guid entry)
    {
        await ctx.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));
        await ctx.Database.ExecuteSqlRawAsync(LineSql(entry, 1, s.CashId, 100m, 0m));
        await ctx.Database.ExecuteSqlRawAsync(LineSql(entry, 2, s.RevenueId, 0m, 100m));
        await ctx.Database.ExecuteSqlRawAsync(FlipSql(s, entry));
    }

    private static async Task PostUnbalancedAsync(ApplicationDbContext ctx, Scaffold s, Guid entry)
    {
        await ctx.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));
        await ctx.Database.ExecuteSqlRawAsync(LineSql(entry, 1, s.CashId, 100m, 0m));
        await ctx.Database.ExecuteSqlRawAsync(FlipSql(s, entry));
    }

    // -------------------------------------------------------------------------------------
    // D-05 - a posted entry must balance.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Posted_Entry_That_Balances_Is_Accepted()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();

        var error = await ErrorOfAsync(() => PostBalancedAsync(context, s, Guid.NewGuid()));

        error.Should().BeNull();
    }

    [Fact]
    public async Task A_Posted_Entry_That_Does_Not_Balance_Is_Refused_At_Commit()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();

        var error = await ErrorOfAsync(() => PostUnbalancedAsync(context, s, Guid.NewGuid()));

        error.Should().NotBeNull();
        error!.Should().Contain("unbalanced");
    }

    /// <summary>
    /// A bookkeeper composes an entry over several statements, so a draft may be unbalanced while it
    /// is being written. Only posting is required to balance - which is why the trigger is deferred
    /// to COMMIT and carries a draft guard.
    /// </summary>
    [Fact]
    public async Task A_Draft_Entry_May_Be_Transiently_Unbalanced()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();
        var entry = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));
        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(LineSql(entry, 1, s.CashId, 100m, 0m)));

        error.Should().BeNull();
    }

    // -------------------------------------------------------------------------------------
    // D-04 - a posted entry's money is frozen.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Line_Cannot_Be_Added_To_A_Posted_Entry()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();
        var entry = Guid.NewGuid();
        await PostBalancedAsync(context, s, entry);

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(LineSql(entry, 3, s.CashId, 500m, 0m)));

        error.Should().NotBeNull();
        error!.Should().Contain("frozen");
    }

    [Fact]
    public async Task A_Line_Cannot_Be_Deleted_From_A_Posted_Entry()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();
        var entry = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));
        var lineId = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO journal_lines (id, entry_id, line_no, account_id, debit_uah, credit_uah) VALUES ('{lineId}', '{entry}', 1, '{s.CashId}', 100.00, 0)");
        await context.Database.ExecuteSqlRawAsync(LineSql(entry, 2, s.RevenueId, 0m, 100m));
        await context.Database.ExecuteSqlRawAsync(FlipSql(s, entry));

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync($"DELETE FROM journal_lines WHERE id = '{lineId}'"));

        error.Should().NotBeNull();
        error!.Should().Contain("frozen");
    }

    /// <summary>
    /// The other side of the freeze, and the reason the guard is not simply "refuse everything":
    /// while the entry is a draft its lines can be added, changed and removed freely.
    /// </summary>
    [Fact]
    public async Task A_Draft_Entry_Can_Still_Grow()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();
        var entry = Guid.NewGuid();

        await context.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));
        await context.Database.ExecuteSqlRawAsync(LineSql(entry, 1, s.CashId, 40m, 0m));
        await context.Database.ExecuteSqlRawAsync(LineSql(entry, 2, s.RevenueId, 0m, 40m));
        await context.Database.ExecuteSqlRawAsync($"DELETE FROM journal_lines WHERE entry_id = '{entry}' AND line_no = 2");
        await context.Database.ExecuteSqlRawAsync(LineSql(entry, 3, s.RevenueId, 0m, 40m));

        var count = await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM journal_lines WHERE entry_id = {entry}").FirstAsync();
        count.Should().Be(2);
    }

    // -------------------------------------------------------------------------------------
    // D-04 - a closed period takes no new postings.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Entry_Cannot_Be_Posted_Into_A_Closed_Period()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync(
            $"UPDATE accounting_periods SET status = 'Closed', closed_at_utc = now(), closed_by_user_id = '{s.PosterId}' WHERE id = '{s.PeriodId}'");

        var error = await ErrorOfAsync(() => PostBalancedAsync(context, s, Guid.NewGuid()));

        error.Should().NotBeNull();
        error!.Should().Contain("closed");
    }

    [Fact]
    public async Task An_Entry_Dated_Outside_Its_Period_Cannot_Be_Posted()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();

        // First day of the month after the period, posted into the period itself.
        var error = await ErrorOfAsync(() => PostBalancedAsync(context, s, Guid.NewGuid()));

        error.Should().BeNull();

        var outside = await ErrorOfAsync(async () =>
        {
            var entry = Guid.NewGuid();
            await context.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.NextMonthDate, entry_no: 2));
            await context.Database.ExecuteSqlRawAsync(LineSql(entry, 1, s.CashId, 100m, 0m));
            await context.Database.ExecuteSqlRawAsync(LineSql(entry, 2, s.RevenueId, 0m, 100m));
            await context.Database.ExecuteSqlRawAsync(FlipSql(s, entry));
        });

        outside.Should().NotBeNull();
        outside!.Should().Contain("outside period");
    }

    // -------------------------------------------------------------------------------------
    // The shape itself.
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Line_May_Not_Carry_Both_A_Debit_And_A_Credit()
    {
        var cash = await SeedAccountAsync("Asset", "Debit");
        await using var context = CreateContext();

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(LineSql(Guid.NewGuid(), 1, cash, 100m, 100m)));

        error.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Period_Cannot_Be_Closed_Without_Naming_Who_Closed_It()
    {
        await using var context = CreateContext();
        var period = Guid.NewGuid();
        var (start, end) = MonthRange(2029, 1);

        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO accounting_periods (id, period_start, period_end, status, closed_at_utc) VALUES ('{period}', '{start:yyyy-MM-dd}', '{end:yyyy-MM-dd}', 'Closed', now())"));

        error.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Posted_Entry_Must_Name_Its_Author()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();
        var entry = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(EntrySql(s, entry, s.Date));

        // Posted with a moment but nobody behind it.
        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            $"UPDATE journal_entries SET status = 'Posted', posted_at_utc = now() WHERE id = '{entry}'"));

        error.Should().NotBeNull();
    }

    [Fact]
    public async Task Two_Entries_In_One_Period_Cannot_Share_An_Entry_Number()
    {
        var s = await SeedAsync();
        await using var context = CreateContext();

        await PostBalancedAsync(context, s, Guid.NewGuid());

        // Every entry here is number 1, which is exactly the collision being tested.
        var error = await ErrorOfAsync(() => PostBalancedAsync(context, s, Guid.NewGuid()));

        error.Should().NotBeNull();
    }
}