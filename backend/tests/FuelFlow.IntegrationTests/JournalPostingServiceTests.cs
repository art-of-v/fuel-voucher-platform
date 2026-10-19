using FluentAssertions;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The posting engine: the first code in the project that can write to the journal.
/// <para>
/// The point of every test here is the same and it is not "does the arithmetic work" - the database
/// already guarantees the sum balances, and <see cref="DoubleEntryJournalTests"/> proves it refuses
/// anything else. The point is that <b>no account code appears in the engine</b>. Accounts arrive as
/// data through <c>ledger_mapping_rules</c>, which is what lets the chart of accounts be replaced
/// when the accountant delivers one without a code change.
/// </para>
/// <para>
/// <strong>No per-test cleanup</strong>, for the same reason as the journal tests it sits beside: a
/// posted entry's lines cannot be deleted, so a cleanup would be refused by the very trigger it was
/// cleaning up. The suite gives this class its own clone of the migrated schema and discards it.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class JournalPostingServiceTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public JournalPostingServiceTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private JournalPostingService CreateService(ApplicationDbContext db)
        => new(db, NullLogger<JournalPostingService>.Instance);

    private static int _codeSeq = 4000;
    private static int _eventSeq;
    private static int _periodSeq;

    /// <summary>Account codes are unique and every test shares one database.</summary>
    private static string NewCode() => Interlocked.Increment(ref _codeSeq).ToString();

    /// <summary>Every test gets its own event name, so mapping rules never collide.</summary>
    private static string NewEvent(string prefix) =>
        $"{prefix}.{Interlocked.Increment(ref _eventSeq)}";

    /// <summary>A distinct month per test, so period auto-creation never overlaps.</summary>
    private static DateOnly NewDate() => new DateOnly(2030, 1, 1).AddMonths(Interlocked.Increment(ref _periodSeq));

    private static int _userSeq;

    /// <summary>
    /// A real user row, because journal_entries carries two foreign keys to users and posting
    /// without a person is exactly what ck_journal_entries_posted_shape exists to refuse.
    /// <para>
    /// Synchronous on purpose: the request has to exist <em>before</em> the assertion lambda that
    /// posts it, and an async seed there would have to be awaited outside the lambda anyway.
    /// </para>
    /// </summary>
    private Guid SeedUser()
    {
        using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        connection.Open();

        var id = Guid.NewGuid();
        var phone = $"+38050{Interlocked.Increment(ref _userSeq):D7}";

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc) VALUES (@id, @phone, now(), now())";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("phone", phone);
        command.ExecuteNonQuery();

        return id;
    }

    private async Task<Guid> SeedAccountAsync(string type, string side, bool postable = true, bool active = true)
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active) " +
            $"VALUES ('{id}', '{NewCode()}', 'Рахунок', '{type}', '{side}', {postable}, {active})");
        return id;
    }

    private async Task<string> MapAsync(string sourceEvent, Guid debitAccount, Guid creditAccount)
    {
        await using var context = CreateContext();
        context.LedgerMappingRules.AddRange(
            new LedgerMappingRule
            {
                Id = Guid.NewGuid(),
                SourceType = JournalSourceType.Payment,
                SourceEvent = sourceEvent,
                AccountId = debitAccount,
                Side = NormalBalance.Debit,
            },
            new LedgerMappingRule
            {
                Id = Guid.NewGuid(),
                SourceType = JournalSourceType.Payment,
                SourceEvent = sourceEvent,
                AccountId = creditAccount,
                Side = NormalBalance.Credit,
            });
        await context.SaveChangesAsync();
        return sourceEvent;
    }

    private PostJournalEntryRequest Req(string sourceEvent, Guid sourceId, DateOnly date, decimal amount = 500.00m) =>
        new()
        {
            SourceEvent = sourceEvent,
            SourceType = JournalSourceType.Payment,
            SourceId = sourceId,
            EntryDate = date,
            AmountUah = amount,
            Description = "Тестове проведення",
            PostedByUserId = SeedUser(),
        };

    [Fact]
    public async Task A_Mapped_Event_Posts_One_Balanced_Entry_Numbered_From_One()
    {
        var sourceEvent = NewEvent("balanced");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));

        var date = NewDate();
        var request = Req(sourceEvent, Guid.NewGuid(), date, 1234.56m);

        await using var context = CreateContext();
        var entry = await CreateService(context).PostAsync(request);

        entry.EntryNo.Should().Be(1, "the entry counter starts at one for a new period");
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.SourceEvent.Should().Be(sourceEvent);
        entry.TamperHash.Should().NotBeNullOrEmpty();
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Sum(l => l.DebitUah).Should().Be(1234.56m);
        entry.Lines.Sum(l => l.CreditUah).Should().Be(1234.56m);
    }

    [Fact]
    public async Task Posting_The_Same_Fact_Twice_Returns_The_First_Entry()
    {
        var sourceEvent = NewEvent("idempotent");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));

        var sourceId = Guid.NewGuid();
        var request = Req(sourceEvent, sourceId, NewDate());

        await using var context = CreateContext();
        var service = CreateService(context);
        var first = await service.PostAsync(request);
        var second = await service.PostAsync(request);

        second.Id.Should().Be(first.Id, "a replayed webhook must not post a second time");
    }

    [Fact]
    public async Task Two_Events_On_One_Source_Row_Post_Two_Entries()
    {
        // This is why source_event was added: a payment legitimately produces revenue, VAT and cost
        // postings. Keying on the source row alone would swallow the second as a duplicate.
        var revenue = NewEvent("revenue");
        var vat = NewEvent("vat");
        var debitAccount = await SeedAccountAsync("Asset", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");
        await MapAsync(revenue, debitAccount, creditAccount);
        await MapAsync(vat, debitAccount, creditAccount);

        var sourceId = Guid.NewGuid();
        var date = NewDate();

        await using var context = CreateContext();
        var service = CreateService(context);
        var revenueEntry = await service.PostAsync(Req(revenue, sourceId, date));
        var vatEntry = await service.PostAsync(Req(vat, sourceId, date));

        revenueEntry.Id.Should().NotBe(vatEntry.Id);
        vatEntry.SourceId.Should().Be(revenueEntry.SourceId);
    }

    [Fact]
    public async Task An_Unmapped_Event_Is_Refused()
    {
        await using var context = CreateContext();

        var act = () => CreateService(context).PostAsync(Req(NewEvent("unmapped"), Guid.NewGuid(), NewDate()));

        await act.Should().ThrowAsync<LedgerUnmappedEventException>();
    }

    [Fact]
    public async Task An_Event_Mapped_Only_On_One_Side_Is_Refused()
    {
        var sourceEvent = NewEvent("half");
        var debitAccount = await SeedAccountAsync("Asset", "Debit");
        await using (var context = CreateContext())
        {
            context.LedgerMappingRules.Add(new LedgerMappingRule
            {
                Id = Guid.NewGuid(),
                SourceType = JournalSourceType.Payment,
                SourceEvent = sourceEvent,
                AccountId = debitAccount,
                Side = NormalBalance.Debit,
            });
            await context.SaveChangesAsync();
        }

        await using var postContext = CreateContext();
        var act = () => CreateService(postContext).PostAsync(Req(sourceEvent, Guid.NewGuid(), NewDate()));

        await act.Should().ThrowAsync<LedgerUnmappedEventException>()
            .WithMessage("*credit*");
    }

    [Fact]
    public async Task A_Header_Account_Is_Refused()
    {
        // "1000 Assets" is a grouping, not a place to post. Nothing in the schema stops a line naming
        // one, which is exactly why this is enforced here.
        var sourceEvent = NewEvent("header");
        var header = await SeedAccountAsync("Asset", "Debit", postable: false);
        await MapAsync(sourceEvent, header, await SeedAccountAsync("Revenue", "Credit"));

        await using var context = CreateContext();
        var act = () => CreateService(context).PostAsync(Req(sourceEvent, Guid.NewGuid(), NewDate()));

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*header*");
    }

    [Fact]
    public async Task An_Inactive_Account_Is_Refused()
    {
        var sourceEvent = NewEvent("inactive");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit", active: false),
            await SeedAccountAsync("Revenue", "Credit"));

        await using var context = CreateContext();
        var act = () => CreateService(context).PostAsync(Req(sourceEvent, Guid.NewGuid(), NewDate()));

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*inactive*");
    }

    [Fact]
    public async Task A_Closed_Period_Is_Refused()
    {
        var sourceEvent = NewEvent("closed");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));

        var date = NewDate();
        var closer = SeedUser();
        await using (var context = CreateContext())
        {
            context.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = Guid.NewGuid(),
                PeriodStart = new DateOnly(date.Year, date.Month, 1),
                PeriodEnd = new DateOnly(date.Year, date.Month, 1).AddMonths(1).AddDays(-1),
                Status = PeriodStatus.Closed,
                ClosedAtUtc = DateTime.UtcNow,
                // ck_accounting_periods_closed_shape requires a person as well as a moment: a period
                // nobody closed cannot be reopened with a straight face.
                ClosedByUserId = closer,
            });
            await context.SaveChangesAsync();
        }

        await using var postContext = CreateContext();
        var act = () => CreateService(postContext).PostAsync(Req(sourceEvent, Guid.NewGuid(), date));

        await act.Should().ThrowAsync<LedgerPeriodClosedException>();
    }

    [Fact]
    public async Task Entry_Numbers_Advance_Within_A_Period_And_Start_A_Again_In_The_Next()
    {
        var sourceEvent = NewEvent("numbers");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));

        var firstMonth = new DateOnly(2031, 6, 15);
        var nextMonth = new DateOnly(2031, 7, 15);

        await using var context = CreateContext();
        var service = CreateService(context);

        var a = await service.PostAsync(Req(sourceEvent, Guid.NewGuid(), firstMonth));
        var b = await service.PostAsync(Req(sourceEvent, Guid.NewGuid(), firstMonth));
        var c = await service.PostAsync(Req(sourceEvent, Guid.NewGuid(), nextMonth));

        a.EntryNo.Should().Be(1);
        b.EntryNo.Should().Be(2, "entry numbers are sequential inside a period");
        c.EntryNo.Should().Be(1, "two periods may each hold entry 1");
    }

    [Fact]
    public async Task The_Credit_Leg_Is_A_Credit_Even_For_An_Asset_Account()
    {
        // The side comes from the leg, not from the account's normal balance: a refund debits cash
        // and credits revenue, and a reversal can credit an account that is normally debited.
        var sourceEvent = NewEvent("reversed-side");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Asset", "Debit"));

        await using var context = CreateContext();
        var entry = await CreateService(context).PostAsync(Req(sourceEvent, Guid.NewGuid(), NewDate(), 42m));

        var debit = entry.Lines.Single(l => l.DebitUah > 0);
        var credit = entry.Lines.Single(l => l.CreditUah > 0);

        credit.Account!.NormalBalance.Should().Be(NormalBalance.Debit);
        credit.CreditUah.Should().Be(42m, "the mapping's side wins over the account's normal balance");
        debit.DebitUah.Should().Be(42m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public async Task A_Non_Positive_Amount_Is_Refused(decimal amount)
    {
        var sourceEvent = NewEvent("bad-amount");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));

        await using var context = CreateContext();
        var act = () => CreateService(context).PostAsync(Req(sourceEvent, Guid.NewGuid(), NewDate(), amount));

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*greater than zero*");
    }
}