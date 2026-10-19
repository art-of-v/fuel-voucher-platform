using FluentAssertions;
using FuelFlow.Features.Ledger;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Proves the seeded chart and mapping are real rather than plausible: it posts a business fact
/// through <c>JournalPostingService</c> and reads back the accounts by name.
/// </summary>
/// <remarks>
/// The rest of the ledger suite builds its own accounts, because it tests the engine. This class
/// tests the <b>data</b> - that a payment lands on cash and an advance rather than on revenue, that
/// redemption releases the advance, and that the VAT contra-account takes both sides. A fabricated
/// chart would pass every engine test and fail these.
/// </remarks>
[Collection("Integration Tests")]
public sealed class SeededChartOfAccountsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public SeededChartOfAccountsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private JournalPostingService CreateService(ApplicationDbContext db)
        => new(db, NullLogger<JournalPostingService>.Instance);

    private static int _seq;

    private PostJournalEntryRequest Req(string sourceEvent, JournalSourceType type, decimal amount) =>
        new()
        {
            // The real seeded event name, not a synthetic one: the point is to exercise the mapping
            // that ships. source_id stays unique per call so the idempotency key is.
            SourceEvent = sourceEvent,
            SourceType = type,
            SourceId = Guid.NewGuid(),
            EntryDate = new DateOnly(2032, 1, 10),
            AmountUah = amount,
            Description = "seeded chart",
            PostedByUserId = SeedUser(),
        };

    private Guid SeedUser()
    {
        using var connection = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        connection.Open();
        var id = Guid.NewGuid();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO users (id, phone_number, created_at_utc, updated_at_utc) VALUES (@id, @phone, now(), now())";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("phone", $"+38067{Interlocked.Increment(ref _seq):D7}");
        command.ExecuteNonQuery();
        return id;
    }

    private async Task<Dictionary<string, string>> CodesForAsync(Guid entryId)
    {
        await using var context = CreateContext();
        return await context.JournalLines
            .Where(l => l.EntryId == entryId)
            .Select(l => new { l.Account!.Code, l.Account.NameUkr, l.DebitUah, l.CreditUah })
            .ToListAsync()
            .ContinueWith(t => t.Result.ToDictionary(
                x => x.DebitUah > 0 ? "debit" : "credit",
                x => $"{x.Code} {x.NameUkr}"));
    }

    [Fact]
    public async Task Every_Seeded_Account_Matches_MinFin_Order_291()
    {
        await using var context = CreateContext();

        var accounts = await context.ChartOfAccounts.AsNoTracking().ToListAsync();

        accounts.Should().HaveCount(24, "11 headers plus 13 subaccounts, and only what this business uses");

        // Spot-checks against the plan rather than an exhaustive list: these are the codes whose
        // meaning the earlier fabricated table got wrong.
        async Task<string?> CodeOf(string code) => (await context.ChartOfAccounts
            .FirstOrDefaultAsync(a => a.Code == code))?.NameUkr;

        (await CodeOf("1010")).Should().BeNullOrEmpty("1010 is fixed assets, not stock - it is not seeded");
        (await CodeOf("281")).Should().Be("Товари на складі");
        (await CodeOf("311")).Should().Be("Поточні рахунки в національній валюті");
        (await CodeOf("401")).Should().Be("Статутний капітал");
        (await CodeOf("643")).Should().Be("Податкові зобов'язання");
        (await CodeOf("702")).Should().Be("Дохід від реалізації товарів");
        (await CodeOf("703")).Should().Be("Вирахування з доходу");
        (await CodeOf("808")).Should().Be("Витрати товарів");
    }

    [Fact]
    public async Task Synthetic_Accounts_Are_Headers_And_Their_Subaccounts_Are_Postable()
    {
        await using var context = CreateContext();

        var header = await context.ChartOfAccounts.FirstAsync(a => a.Code == "68");
        var subAccount = await context.ChartOfAccounts.FirstAsync(a => a.Code == "681");

        header.IsPostable.Should().BeFalse("68 is a grouping; only subaccounts take postings");
        subAccount.IsPostable.Should().BeTrue("681 is where the advance actually lives");

        // And every account the seeded mapping points at is postable, so no seeded event can be
        // refused by the header check the engine applies.
        var mapped = await context.LedgerMappingRules
            .Include(r => r.Account)
            .AsNoTracking()
            .ToListAsync();

        mapped.Should().NotBeEmpty();
        mapped.Should().OnlyContain(r => r.Account!.IsPostable,
            "a mapping onto a header account is refused by the engine at posting time");
    }

    [Fact]
    public async Task A_Payment_Lands_On_Bank_Cash_And_An_Advance_Not_On_Revenue()
    {
        var request = Req("payment.received", JournalSourceType.Payment, 1000m);

        await using var context = CreateContext();
        var entry = await CreateService(context).PostAsync(request);

        var codes = await CodesForAsync(entry.Id);
        codes["debit"].Should().StartWith("311", "the money arrives in the bank account");
        codes["credit"].Should().StartWith("681", "revenue is not recognised until the fuel is drawn");
    }

    [Fact]
    public async Task Redemption_Releases_The_Advance_Into_Revenue()
    {
        var request = Req("voucher.redeemed.revenue", JournalSourceType.Fulfillment, 800m);

        await using var context = CreateContext();
        var entry = await CreateService(context).PostAsync(request);

        var codes = await CodesForAsync(entry.Id);
        codes["debit"].Should().StartWith("681");
        codes["credit"].Should().StartWith("702");
    }

    [Fact]
    public async Task The_Vat_Contra_Account_Takes_Both_Sides()
    {
        // 703 is debited when VAT is carved out of an advance and credited when it is carved out of
        // a sale. An engine that derived the side from the account's normal balance would produce a
        // 703 credit with no matching debit.
        await using (var context = CreateContext())
        {
            var entry = await CreateService(context).PostAsync(Req("payment.received.vat", JournalSourceType.Payment, 200m));
            var codes = await CodesForAsync(entry.Id);
            codes["debit"].Should().StartWith("703");
            codes["credit"].Should().StartWith("643");
        }

        await using var second = CreateContext();
        var redemption = await CreateService(second).PostAsync(Req("voucher.redeemed.vat", JournalSourceType.Fulfillment, 160m));
        var redemptionCodes = await CodesForAsync(redemption.Id);
        redemptionCodes["debit"].Should().StartWith("643");
        redemptionCodes["credit"].Should().StartWith("703");
    }

    [Fact]
    public async Task Stock_Cost_Goes_To_The_Element_Account_And_Never_To_A_Header()
    {
        await using var context = CreateContext();
        var entry = await CreateService(context)
            .PostAsync(Req("voucher.redeemed.cogs", JournalSourceType.Fulfillment, 700m));

        var codes = await CodesForAsync(entry.Id);
        codes["debit"].Should().StartWith("808", "cost of goods sold is an expense by element");
        codes["credit"].Should().StartWith("281", "inventory leaves the warehouse");
    }

    [Fact]
    public async Task Every_Seeded_Event_Has_Both_Sides_Or_It_Is_Not_Mapped()
    {
        await using var context = CreateContext();

        var sides = await context.LedgerMappingRules
            .AsNoTracking()
            .GroupBy(r => r.SourceEvent)
            .Select(g => new { Event = g.Key, Sides = g.Select(r => r.Side).Distinct().Count() })
            .ToListAsync();

        sides.Should().NotBeEmpty();
        sides.Should().OnlyContain(s => s.Sides == 2,
            "an event mapped on one side only is a hole in the books, not a posting");
    }
}