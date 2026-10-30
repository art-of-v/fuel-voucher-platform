using FluentAssertions;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.ManualEntries;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The manual journal entry: a posting a person composes (planning #795).
/// </summary>
/// <remarks>
/// <para>
/// The engine's other paths take their accounts from <c>ledger_mapping_rules</c>; this one takes them
/// from the caller, because a reclassification is a decision and the accounts are the decision. What
/// every other posting guarantees still has to hold here - the entry balances, the number comes from
/// the period's sequence, a closed month takes nothing, posting records who did it, and a posted
/// entry is frozen - and that is what these tests are about.
/// </para>
/// <para>
/// <strong>No per-test cleanup</strong>, for the same reason as the journal tests beside it: a posted
/// entry's lines cannot be deleted, so a cleanup would be refused by the very trigger it was cleaning
/// up. Every test takes unique account codes, a unique reference and a unique accounting month.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class ManualJournalEntryTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public ManualJournalEntryTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private JournalPostingService CreateService(ApplicationDbContext db)
        => new(db, NullLogger<JournalPostingService>.Instance);

    private static int _codeSeq = 6000;
    private static int _periodSeq;
    private static int _userSeq;

    private static string NewCode() => Interlocked.Increment(ref _codeSeq).ToString();

    /// <summary>A distinct month per test: automatic period creation must never overlap.</summary>
    private static DateOnly NewDate() => new DateOnly(2035, 1, 1).AddMonths(Interlocked.Increment(ref _periodSeq));

    private Guid SeedUser()
    {
        using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        connection.Open();

        var id = Guid.NewGuid();
        var phone = $"+38066{Interlocked.Increment(ref _userSeq):D7}";

        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc) VALUES (@id, @phone, now(), now())";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("phone", phone);
        command.ExecuteNonQuery();

        return id;
    }

    private async Task<string> SeedAccountAsync(string type, string side, bool postable = true, bool active = true)
    {
        await using var context = CreateContext();
        var code = NewCode();
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO chart_of_accounts (id, code, name_ukr, account_type, normal_balance, is_postable, is_active) " +
            $"VALUES ('{Guid.NewGuid()}', '{code}', 'Рахунок {code}', '{type}', '{side}', {postable}, {active})");
        return code;
    }

    /// <summary>A month closed before the entry is asked for.</summary>
    private async Task CloseMonthAsync(DateOnly date)
    {
        var closer = SeedUser();
        var start = new DateOnly(date.Year, date.Month, 1);

        await using var context = CreateContext();
        context.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = Guid.NewGuid(),
            PeriodStart = start,
            PeriodEnd = start.AddMonths(1).AddDays(-1),
            Status = PeriodStatus.Closed,
            ClosedAtUtc = TestClock.UtcNow,
            ClosedByUserId = closer,
        });
        await context.SaveChangesAsync();
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

    private static string NewReference() => $"test-{Guid.NewGuid():N}";

    [Fact]
    public async Task A_Manual_Entry_Posts_Every_Leg_Balanced_And_Numbered()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");
        var secondDebit = await SeedAccountAsync("Asset", "Debit");
        var date = NewDate();
        var userId = SeedUser();

        await using var context = CreateContext();
        var entry = await CreateService(context).PostManualAsync(
            Manual(date, NewReference(), "D3: VAT deduction moved from 703 to 704",
                Dr(debitAccount, 2298.69m),
                Dr(secondDebit, 701.31m),
                Cr(creditAccount, 3000.00m)),
            userId);

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.SourceEvent.Should().Be(JournalPostingService.ManualAdjustmentEvent);
        entry.SourceType.Should().Be(JournalSourceType.Adjustment);
        entry.Description.Should().Be("D3: VAT deduction moved from 703 to 704");
        entry.EntryNo.Should().Be(1, "the entry counter starts at one for a new period");
        entry.TamperHash.Should().NotBeNullOrEmpty("a posted entry carries its checksum");
        entry.PostedAtUtc.Should().NotBeNull();

        // A posting nobody can be asked about is not a posting this ledger takes.
        entry.PostedByUserId.Should().Be(userId);
        entry.CreatedByUserId.Should().Be(userId);

        entry.Lines.Should().HaveCount(3);
        entry.Lines.Select(l => l.LineNo).Should().Equal(1, 2, 3);
        entry.Lines.Sum(l => l.DebitUah).Should().Be(3000.00m);
        entry.Lines.Sum(l => l.CreditUah).Should().Be(3000.00m);
    }

    [Fact]
    public async Task The_Same_Reference_Returns_The_Entry_The_First_Call_Wrote()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");
        var date = NewDate();
        var reference = NewReference();

        await using var context = CreateContext();
        var service = CreateService(context);

        var first = await service.PostManualAsync(
            Manual(date, reference, "D3 reclassification", Dr(debitAccount, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        // A retried request: same reference, and even a different body must land on the first entry
        // rather than post a second, identical-looking adjustment.
        var second = await service.PostManualAsync(
            Manual(date, reference, "D3 reclassification (retry)", Dr(debitAccount, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        second.Id.Should().Be(first.Id);
        second.EntryNo.Should().Be(first.EntryNo);

        var written = await context.JournalEntries
            .CountAsync(e => e.SourceEvent == JournalPostingService.ManualAdjustmentEvent
                          && e.SourceId == first.SourceId);
        written.Should().Be(1, "a replay must not post the same adjustment twice");
    }

    [Fact]
    public async Task A_Different_Reference_Posts_A_Second_Entry()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");
        var date = NewDate();

        await using var context = CreateContext();
        var service = CreateService(context);

        var first = await service.PostManualAsync(
            Manual(date, NewReference(), "First adjustment", Dr(debitAccount, 10m), Cr(creditAccount, 10m)),
            SeedUser());
        var second = await service.PostManualAsync(
            Manual(date, NewReference(), "Second adjustment", Dr(debitAccount, 20m), Cr(creditAccount, 20m)),
            SeedUser());

        second.Id.Should().NotBe(first.Id);
        second.EntryNo.Should().Be(first.EntryNo + 1, "the period's numbers stay a gapless sequence");
    }

    [Fact]
    public async Task An_Unbalanced_Entry_Is_Refused()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Does not balance",
                Dr(debitAccount, 100m), Cr(creditAccount, 99.99m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*does not balance*");
    }

    [Fact]
    public async Task A_Single_Leg_Is_Refused()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "One leg", Dr(debitAccount, 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*at least two legs*");
    }

    [Fact]
    public async Task A_Leg_Must_Fill_Exactly_One_Side()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();
        var service = CreateService(context);
        var date = NewDate();

        var bothSides = () => service.PostManualAsync(
            Manual(date, NewReference(), "Both sides",
                new ManualJournalLineRequest { AccountCode = debitAccount, DebitUah = 50m, CreditUah = 50m },
                Cr(creditAccount, 50m)),
            SeedUser());
        var neitherSide = () => service.PostManualAsync(
            Manual(date, NewReference(), "Neither side",
                new ManualJournalLineRequest { AccountCode = debitAccount },
                Cr(creditAccount, 50m)),
            SeedUser());

        await bothSides.Should().ThrowAsync<LedgerPostingException>().WithMessage("*exactly one side*");
        await neitherSide.Should().ThrowAsync<LedgerPostingException>().WithMessage("*exactly one side*");
    }

    [Fact]
    public async Task An_Unknown_Account_Code_Is_Refused_And_Named()
    {
        var known = await SeedAccountAsync("Vat", "Debit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Typo in the account", Dr(known, 100m), Cr("99999999", 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*99999999*");
    }

    [Fact]
    public async Task A_Header_Account_Is_Refused()
    {
        var header = await SeedAccountAsync("Asset", "Debit", postable: false);
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Posts to a header", Dr(header, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*header*");
    }

    [Fact]
    public async Task An_Inactive_Account_Is_Refused()
    {
        var retired = await SeedAccountAsync("Asset", "Debit", active: false);
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Posts to a retired account", Dr(retired, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*inactive*");
    }

    [Fact]
    public async Task A_Closed_Period_Takes_No_Manual_Entry()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");
        var date = NewDate();
        await CloseMonthAsync(date);

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(date, NewReference(), "Late adjustment", Dr(debitAccount, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        // Closed means closed: the fix is an entry in an open period, not an edit to a signed-off month.
        await act.Should().ThrowAsync<LedgerPeriodClosedException>();
    }

    [Fact]
    public async Task A_Manual_Entry_Without_A_Reason_Is_Refused()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "   ", Dr(debitAccount, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*say why*");
    }

    [Fact]
    public async Task A_Manual_Entry_That_Is_Not_Whole_Kopecks_Is_Refused()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "A third of a kopeck",
                Dr(debitAccount, 10.005m), Cr(creditAccount, 10.005m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*whole kopecks*");
    }

    [Fact]
    public async Task A_Leg_That_Moves_No_Quantity_Is_Refused()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();

        var act = () => CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Zero litres",
                new ManualJournalLineRequest { AccountCode = debitAccount, DebitUah = 100m, QuantityLiters = 0m },
                Cr(creditAccount, 100m)),
            SeedUser());

        await act.Should().ThrowAsync<LedgerPostingException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public async Task A_Posted_Manual_Entry_Is_Frozen()
    {
        var debitAccount = await SeedAccountAsync("Vat", "Debit");
        var creditAccount = await SeedAccountAsync("Revenue", "Credit");

        await using var context = CreateContext();
        var entry = await CreateService(context).PostManualAsync(
            Manual(NewDate(), NewReference(), "Frozen once posted", Dr(debitAccount, 100m), Cr(creditAccount, 100m)),
            SeedUser());

        var lineId = entry.Lines.First().Id;
        var error = await ErrorOfAsync(() => context.Database.ExecuteSqlRawAsync(
            $"UPDATE journal_lines SET debit_uah = debit_uah + 1 WHERE id = '{lineId}'"));

        error.Should().NotBeNull("a correction is a second entry, never an edit to a posted one");
        error!.Should().Contain("frozen");
    }

    private static async Task<string?> ErrorOfAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.MessageText;
        }
    }
}
