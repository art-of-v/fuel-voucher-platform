using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The minimum-discount knob is gone from the schema, and the CHECK that guards the price knobs
/// survives without it.
/// </summary>
/// <remarks>
/// <para>
/// The price is <c>cost + margin</c> and the column price beside it is a reference the customer is
/// shown against - never a term of our price (planning <c>docs/PRICING_MODEL.md</c>, 2026-10-21).
/// The knob under the column (<c>min(cost + margin, pump - min discount)</c>) was the last piece of
/// the old formula, and it was a second dial on one number: four writers kept copies of it, and the
/// admin card fed a displayed saving back into it as a stored setting.
/// </para>
/// <para>
/// The migration that drops the column has to drop the CHECK that names it first and put the CHECK
/// back afterwards, because PostgreSQL drops a constraint that references a dropped column on its
/// own - and this one has to survive: it still guards the cost and the column price. These two
/// assertions are that migration's proof, read out of the database rather than out of the model.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class MinimumDiscountColumnTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MinimumDiscountColumnTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Fact]
    public async Task The_Fuel_Table_Has_No_Minimum_Discount_Column()
    {
        await using var context = CreateContext();

        var columns = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'fuel_types'
                  AND column_name = 'min_discount_per_liter'
                """)
            .ToListAsync();

        columns.Should().BeEmpty(
            "the knob took part in no price anybody paid after 2026-10-21, and the model does not "
            + "carry it - a column left behind is a dial on a number the price does not read");
    }

    [Fact]
    public async Task The_Pricing_Knobs_Constraint_Survives_Without_Naming_It()
    {
        await using var context = CreateContext();

        var definitions = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT pg_get_constraintdef(oid) AS "Value"
                FROM pg_constraint
                WHERE conname = 'ck_fuel_types_pricing_knobs_non_negative'
                """)
            .ToListAsync();

        definitions.Should().ContainSingle(
            "the constraint guards the cost and the column price, and dropping the column must not "
            + "take it with it");

        var definition = definitions.Single();
        definition.Should().NotContain("min_discount_per_liter");
        definition.Should().Contain("supplier_price_per_liter");
        definition.Should().Contain("pump_price_per_liter");
    }
}
