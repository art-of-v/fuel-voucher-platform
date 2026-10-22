using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The two stored copies of the price are gone from <c>fuel_types</c>, and the CHECK that guarded
/// them is gone with them.
/// </summary>
/// <remarks>
/// <para>
/// The price is <c>cost + margin</c> and the column price beside it is a reference the customer is
/// shown against - never a term of our price (planning <c>docs/PRICING_MODEL.md</c>, 2026-10-21).
/// The copies of those numbers - <c>base_price</c> and <c>discount_price</c> - had four writers
/// keeping them in step by hand, and they disagreed: fourteen of the twenty-three fuels on
/// production carried a zero in the customer price, which is a price that says the customer takes
/// the fuel for nothing.
/// </para>
/// <para>
/// A column that is "retired, kept until the migration that drops it" is a column the next person
/// to open the table reads as the truth, and the same two names in the table and in the public API
/// meant two different facts. These assertions are that migration's proof, read out of the
/// database rather than out of the model.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class RetiredPriceColumnsTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RetiredPriceColumnsTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Theory]
    [InlineData("base_price")]
    [InlineData("discount_price")]
    public async Task The_Fuel_Table_Has_No_Retired_Price_Column(string column)
    {
        await using var context = CreateContext();

        var columns = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'fuel_types'
                  AND column_name = {0}
                """, column)
            .ToListAsync();

        columns.Should().BeEmpty(
            "the price is computed from the knobs, so a stored copy of it is a second place to say "
            + "what one litre costs - and the two places disagreed");
    }

    [Fact]
    public async Task The_Constraint_That_Named_Them_Is_Gone_With_Them()
    {
        await using var context = CreateContext();

        var definitions = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT pg_get_constraintdef(oid) AS "Value"
                FROM pg_constraint
                WHERE conname = 'ck_fuel_types_prices_non_negative'
                """)
            .ToListAsync();

        definitions.Should().BeEmpty(
            "the constraint guarded two columns that do not exist any more - the rules that survive "
            + "are the ones that guard the knobs the price is computed from");
    }

    [Theory]
    [InlineData("ck_fuel_types_pricing_knobs_non_negative", "supplier_price_per_liter")]
    [InlineData("ck_fuel_types_pricing_knobs_non_negative", "pump_price_per_liter")]
    [InlineData("ck_fuel_types_margin_signs", "margin_uah_per_liter")]
    public async Task The_Surviving_Constraints_Still_Guard_The_Knobs(string constraint, string column)
    {
        await using var context = CreateContext();

        var definitions = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT pg_get_constraintdef(oid) AS "Value"
                FROM pg_constraint
                WHERE conname = {0}
                """, constraint)
            .ToListAsync();

        definitions.Should().ContainSingle(
            "the knob the price is computed from is a number somebody wrote, and a negative one of "
            + "them is refused by the database");

        definitions.Single().Should().Contain(column);
    }
}
