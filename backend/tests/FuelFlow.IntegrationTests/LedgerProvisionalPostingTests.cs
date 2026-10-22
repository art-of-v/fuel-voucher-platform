using FluentAssertions;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The provisional posting: an entry that exists before it is in the books.
/// <para>
/// A self-reported redemption is a fact the business has not confirmed yet (planning #684). The
/// entry is composed and numbered at the report, but it stays a draft until the supplier's evidence
/// turns it into a posting - and a report that is withdrawn discards the draft rather than leaving
/// a sale in the books that never happened. These tests pin that behaviour at the database, where
/// the draft/posted boundary is enforced by triggers and check constraints rather than by a
/// convention in the handler.
/// </para>
/// <para>
/// <strong>No per-test cleanup</strong>, for the same reason as the journal tests beside it: a
/// posted entry's lines cannot be deleted, so a cleanup would be refused by the very trigger it was
/// cleaning up. The suite gives this class its own clone of the migrated schema and discards it;
/// every test uses its own accounts, event names and months so the clone is not a collision.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public sealed class LedgerProvisionalPostingTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public LedgerProvisionalPostingTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private JournalPostingService CreateService(ApplicationDbContext db)
        => new(db, NullLogger<JournalPostingService>.Instance);

    private static int _codeSeq = 6000;
    private static int _eventSeq;
    private static int _periodSeq;
    private static int _userSeq;

    /// <summary>Account codes are unique and every test shares one database.</summary>
    private static string NewCode() => Interlocked.Increment(ref _codeSeq).ToString();

    /// <summary>Every test gets its own event name, so mapping rules never collide.</summary>
    private static string NewEvent(string prefix) => $"{prefix}.{Interlocked.Increment(ref _eventSeq)}";

    /// <summary>A distinct month per test, so period auto-creation never overlaps.</summary>
    private static DateOnly NewDate() => new DateOnly(2033, 1, 1).AddMonths(Interlocked.Increment(ref _periodSeq));

    /// <summary>
    /// A real user row, because journal_entries carries two foreign keys to users and posting
    /// without a person is exactly what ck_journal_entries_posted_shape exists to refuse.
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

    private async Task<Guid> SeedAccountAsync(string type, string side)
    {
        await using var context = CreateContext();
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active) " +
            $"VALUES ('{id}', '{NewCode()}', 'Рахунок', '{type}', '{side}', true, true)");
        return id;
    }

    private async Task MapAsync(string sourceEvent, Guid debitAccount, Guid creditAccount)
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

    private async Task<(string Event, Guid SourceId, DateOnly Date)> SeedMappedFactAsync(decimal amount = 500m)
    {
        var sourceEvent = NewEvent("provisional");
        await MapAsync(sourceEvent,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));
        return (sourceEvent, Guid.NewGuid(), NewDate());
    }

    [Fact]
    public async Task A_Provisional_Entry_Is_A_Draft_And_Not_In_The_Books()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync(321.00m);
        var request = Req(sourceEvent, sourceId, date, 321.00m);

        await using var context = CreateContext();
        var entry = await CreateService(context).PostProvisionalAsync(request);

        entry.Status.Should().Be(JournalEntryStatus.Draft);
        entry.PostedAtUtc.Should().BeNull("a report is not a confirmation");
        entry.TamperHash.Should().BeNull("nothing has been attested to yet");
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Sum(l => l.DebitUah).Should().Be(321.00m);

        await using var reader = CreateContext();
        (await reader.JournalEntries.CountAsync(
                e => e.SourceId == sourceId && e.Status == JournalEntryStatus.Posted))
            .Should().Be(0, "a draft is not in the books");
    }

    [Fact]
    public async Task Posting_Provisional_Entry_Twice_Returns_The_First_Draft()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();
        var request = Req(sourceEvent, sourceId, date);

        await using var context = CreateContext();
        var service = CreateService(context);

        var first = await service.PostProvisionalAsync(request);
        var second = await service.PostProvisionalAsync(request);

        second.Id.Should().Be(first.Id, "a replayed report must not compose a second entry");
    }

    [Fact]
    public async Task Confirming_Posts_The_Draft_In_Place()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();
        var request = Req(sourceEvent, sourceId, date);
        var operatorId = SeedUser();

        await using var context = CreateContext();
        var service = CreateService(context);
        var draft = await service.PostProvisionalAsync(request);

        var confirmed = await service.ConfirmProvisionalAsync(sourceId, operatorId);

        confirmed.Should().ContainSingle();
        var entry = confirmed[0];
        entry.Id.Should().Be(draft.Id, "the draft is posted, not copied");
        entry.EntryNo.Should().Be(draft.EntryNo);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.PostedAtUtc.Should().Be(TestClock.UtcNow);
        entry.PostedByUserId.Should().Be(operatorId);
        entry.TamperHash.Should().MatchRegex("^[0-9a-f]{64}$");

        await using var reader = CreateContext();
        (await reader.JournalEntries.CountAsync(e => e.SourceId == sourceId))
            .Should().Be(1, "confirmation changes the draft, it does not add a second entry");
    }

    [Fact]
    public async Task Confirming_Twice_Is_Idempotent()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();
        var operatorId = SeedUser();

        await using var context = CreateContext();
        var service = CreateService(context);
        await service.PostProvisionalAsync(Req(sourceEvent, sourceId, date));

        var first = await service.ConfirmProvisionalAsync(sourceId, operatorId);
        var second = await service.ConfirmProvisionalAsync(sourceId, operatorId);

        second.Should().ContainSingle();
        second[0].Id.Should().Be(first[0].Id);
        second[0].Status.Should().Be(JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task One_Confirmation_Posts_Every_Draft_Of_Its_Source()
    {
        var sourceId = Guid.NewGuid();
        var date = NewDate();

        var revenue = NewEvent("revenue");
        var cogs = NewEvent("cogs");
        await MapAsync(revenue,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Revenue", "Credit"));
        await MapAsync(cogs,
            await SeedAccountAsync("Asset", "Debit"),
            await SeedAccountAsync("Expense", "Credit"));

        await using var context = CreateContext();
        var service = CreateService(context);
        await service.PostProvisionalAsync(Req(revenue, sourceId, date, 100m));
        await service.PostProvisionalAsync(Req(cogs, sourceId, date, 60m));

        var confirmed = await service.ConfirmProvisionalAsync(sourceId, SeedUser());

        confirmed.Should().HaveCount(2, "a redemption posts revenue and cost of sales together");
        confirmed.Should().OnlyContain(e => e.Status == JournalEntryStatus.Posted);
    }

    [Fact]
    public async Task Discarding_Removes_The_Draft_And_Posts_Nothing()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();

        await using var context = CreateContext();
        var service = CreateService(context);
        await service.PostProvisionalAsync(Req(sourceEvent, sourceId, date));

        var discarded = await service.DiscardProvisionalAsync(sourceId);

        discarded.Should().Be(1);

        await using var reader = CreateContext();
        (await reader.JournalEntries.CountAsync(e => e.SourceId == sourceId))
            .Should().Be(0, "a withdrawn report leaves no posting behind");

        await using var after = CreateContext();
        (await CreateService(after).ConfirmProvisionalAsync(sourceId, SeedUser()))
            .Should().BeEmpty("there is nothing left to confirm");
    }

    [Fact]
    public async Task Confirming_After_The_Period_Closed_Reposts_In_The_Open_Period()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync(500m);
        var request = Req(sourceEvent, sourceId, date, 500m);

        JournalEntry draft;
        await using (var context = CreateContext())
        {
            draft = await CreateService(context).PostProvisionalAsync(request);

            // Close the month the draft sits in, the way a month actually closes: a person, a
            // moment, and the status that refuses every later posting into it.
            var closer = SeedUser();
            var closed = await context.AccountingPeriods
                .Where(p => p.Id == draft.PeriodId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PeriodStatus.Closed)
                    .SetProperty(p => p.ClosedAtUtc, TestClock.UtcNow)
                    .SetProperty(p => p.ClosedByUserId, closer));

            closed.Should().Be(1, "the draft's month must exist for this test to mean anything");
        }

        await using (var context = CreateContext())
        {
            var confirmed = await CreateService(context).ConfirmProvisionalAsync(sourceId, SeedUser());

            confirmed.Should().ContainSingle();
            var entry = confirmed[0];

            entry.Id.Should().NotBe(draft.Id,
                "a closed period cannot take the posting, so the draft was discarded and reposted");
            entry.Status.Should().Be(JournalEntryStatus.Posted);
            entry.EntryDate.Should().Be(DateOnly.FromDateTime(TestClock.UtcNow),
                "a late fact lands in the period it arrives in");
            entry.SourceEvent.Should().Be(sourceEvent);
            entry.SourceId.Should().Be(sourceId);
            entry.Lines.Sum(l => l.DebitUah).Should().Be(500m);
            entry.Lines.Sum(l => l.CreditUah).Should().Be(500m);

            (await context.JournalEntries.AnyAsync(e => e.Id == draft.Id))
                .Should().BeFalse("the stranded draft is gone, not left beside its replacement");

            var openPeriod = await context.AccountingPeriods.SingleAsync(
                p => p.PeriodStart == new DateOnly(TestClock.UtcNow.Year, TestClock.UtcNow.Month, 1));
            openPeriod.Status.Should().Be(PeriodStatus.Open);
            entry.PeriodId.Should().Be(openPeriod.Id);
        }
    }

    [Fact]
    public async Task Reversing_Closes_The_Original_In_An_Open_Period()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync(250m);
        var operatorId = SeedUser();

        await using var context = CreateContext();
        var service = CreateService(context);
        var original = await service.PostAsync(Req(sourceEvent, sourceId, date, 250m));

        // The original sits in a month that has already been signed off. That is precisely when a
        // correction has to be a reversal in today's period rather than an edit to that month.
        var closer = SeedUser();
        await context.AccountingPeriods
            .Where(p => p.Id == original.PeriodId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PeriodStatus.Closed)
                .SetProperty(p => p.ClosedAtUtc, TestClock.UtcNow)
                .SetProperty(p => p.ClosedByUserId, closer));

        var reversal = await service.ReverseAsync(original.Id, operatorId, "the supplier statement disproved it");

        reversal.Status.Should().Be(JournalEntryStatus.Posted);
        reversal.SourceType.Should().Be(JournalSourceType.Reversal);
        reversal.SourceEvent.Should().Be("entry.reversed");
        reversal.SourceId.Should().Be(original.Id);
        reversal.EntryDate.Should().Be(DateOnly.FromDateTime(TestClock.UtcNow),
            "the correcting entry belongs to the open period");
        reversal.Description.Should().Contain($"entry {original.EntryNo}");
        reversal.Description.Should().Contain("the supplier statement disproved it");
        reversal.Lines.Sum(l => l.DebitUah).Should().Be(250m);
        reversal.Lines.Sum(l => l.CreditUah).Should().Be(250m);

        var originalDebit = original.Lines.Single(l => l.DebitUah > 0);
        var reversalCredit = reversal.Lines.Single(l => l.CreditUah > 0);
        reversalCredit.AccountId.Should().Be(originalDebit.AccountId,
            "the reversal names the same accounts as the posting it undoes");

        await using var reader = CreateContext();
        var reread = await reader.JournalEntries.SingleAsync(e => e.Id == original.Id);
        reread.Status.Should().Be(JournalEntryStatus.Reversed);
        reread.ReversedByEntryId.Should().Be(reversal.Id);
        reread.EntryDate.Should().Be(date, "the original keeps its date and its number");
    }

    [Fact]
    public async Task A_Draft_Is_Discarded_Not_Reversed()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();

        await using var context = CreateContext();
        var service = CreateService(context);
        var draft = await service.PostProvisionalAsync(Req(sourceEvent, sourceId, date));

        var act = () => service.ReverseAsync(draft.Id, SeedUser(), "not posted yet");

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*Only a posted entry*");
    }

    [Fact]
    public async Task Reversing_The_Same_Entry_Twice_Is_Refused()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();

        await using var context = CreateContext();
        var service = CreateService(context);
        var original = await service.PostAsync(Req(sourceEvent, sourceId, date));

        await service.ReverseAsync(original.Id, SeedUser(), "first correction");

        var act = () => service.ReverseAsync(original.Id, SeedUser(), "second correction");

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*already reversed*");
    }

    [Fact]
    public async Task A_Reversal_Without_A_Reason_Is_Refused()
    {
        var (sourceEvent, sourceId, date) = await SeedMappedFactAsync();

        await using var context = CreateContext();
        var service = CreateService(context);
        var original = await service.PostAsync(Req(sourceEvent, sourceId, date));

        var act = () => service.ReverseAsync(original.Id, SeedUser(), "   ");

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*must say why*");
    }
}
