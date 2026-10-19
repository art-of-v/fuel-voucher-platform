using FluentAssertions;
using FuelFlow.Features.Ledger.SharedModels;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Buying stock is not an expense; selling it is.
/// </summary>
/// <remarks>
/// The mapping shipped with both rules in it - cost at purchase <b>and</b> cost at redemption - so
/// a voucher that was bought and then redeemed was charged twice and left inventory 281 negative.
/// The business settled this on 2026-10-19, in line with IAS 2: inventories are recognised as an
/// expense when sold. This class exists to keep the duplicate from coming back, because nothing in
/// the schema prevents it - the accounting rule lives entirely in the mapping data.
/// </remarks>
[Collection("Integration Tests")]
public sealed class InventoryCostRecognitionTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public InventoryCostRecognitionTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    private async Task<Dictionary<string, string>> MappingsAsync()
    {
        await using var context = CreateContext();
        return await context.LedgerMappingRules
            .AsNoTracking()
            .Select(m => new { m.SourceEvent, Code = m.Account!.Code, m.Side })
            .ToListAsync()
            .ContinueWith(t => t.Result.ToDictionary(
                x => $"{x.SourceEvent}|{x.Side}",
                x => x.Code));
    }

    [Fact]
    public async Task Buying_Stock_Does_Not_Touch_The_Expense_Account()
    {
        var mappings = await MappingsAsync();

        // Whatever the buy-side event is called, it must move value onto 281 and raise a liability
        // or the bank - never debit 808. Asserted against the seed rather than a hard-coded name so
        // this keeps working if the event is renamed.
        var buyEvents = mappings.Keys
            .Select(k => k.Split('|')[0])
            .Where(e => e.StartsWith("stock.purchased", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        buyEvents.Should().NotBeEmpty("buying stock has to be bookable at all");
        buyEvents.Should().NotContain("voucher.redeemed.cogs");

        foreach (var evt in buyEvents)
        {
            var debit = mappings.GetValueOrDefault($"{evt}|Debit");
            debit.Should().NotBe("808",
                $"{evt} must not expense inventory at purchase - that is the double count");
        }
    }

    [Fact]
    public async Task The_Credit_Purchase_Moves_Stock_In_And_A_Payable_Out()
    {
        var mappings = await MappingsAsync();

        mappings["stock.purchased.payable|Debit"].Should().Be("281");
        mappings["stock.purchased.payable|Credit"].Should().Be("631");
    }

    [Fact]
    public async Task The_Cash_Purchase_Moves_Stock_In_And_The_Bank_Out()
    {
        var mappings = await MappingsAsync();

        mappings["stock.purchased.cash|Debit"].Should().Be("281");
        mappings["stock.purchased.cash|Credit"].Should().Be("311");
    }

    [Fact]
    public async Task The_Deleted_Event_Is_Gone()
    {
        var mappings = await MappingsAsync();

        // Named explicitly: this is the mapping that caused the defect, so it is worth asserting by
        // name rather than only as an absence among the stock events.
        mappings.Keys.Should().NotContain(k => k.StartsWith("stock.purchased|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Redemption_Is_Still_Where_Cost_Is_Expensed()
    {
        var mappings = await MappingsAsync();

        mappings["voucher.redeemed.cogs|Debit"].Should().Be("808");
        mappings["voucher.redeemed.cogs|Credit"].Should().Be("281");
    }

    [Fact]
    public async Task Every_Mapped_Buy_Event_Balances_Two_Distinct_Accounts()
    {
        // A buy that mapped both sides to the same account would "balance" while moving nothing.
        var mappings = await MappingsAsync();

        foreach (var evt in mappings.Keys.Select(k => k.Split('|')[0])
                     .Where(e => e.StartsWith("stock.purchased", StringComparison.Ordinal))
                     .Distinct())
        {
            mappings[$"{evt}|Debit"].Should().NotBe(mappings[$"{evt}|Credit"],
                $"{evt} moving value from an account to itself books nothing");
        }
    }
}