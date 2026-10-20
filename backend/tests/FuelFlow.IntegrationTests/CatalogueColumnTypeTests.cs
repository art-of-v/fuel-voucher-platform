using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The catalogue's columns are <c>varchar(n)</c>, like every other column in the schema.
/// </summary>
/// <remarks>
/// Four catalogue columns were <c>text</c> while every other catalogue column is
/// <c>varchar(n)</c>: <c>fuel_nominals.data_kind</c>, <c>fuel_types.name</c>,
/// <c>fuel_types.brand_fuel_code</c> and <c>fuel_type_product_codes.product_code</c>.
///
/// Each one was already bounded by a CHECK constraint, so nothing was wrong with them - the
/// column and the CHECK said the same thing twice. The schema's own convention is
/// <c>varchar(n)</c>, and these four were the exceptions.
///
/// The test reads the types out of the catalogue rather than out of the model: a migration that
/// says it narrowed a column and a database that still has <c>text</c> would otherwise look the
/// same from the application.
/// </remarks>
[Collection("Integration Tests")]
public sealed class CatalogueColumnTypeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public CatalogueColumnTypeTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    [Theory]
    [InlineData("fuel_nominals", "data_kind", 10)]
    [InlineData("fuel_types", "name", 200)]
    [InlineData("fuel_types", "brand_fuel_code", 50)]
    [InlineData("fuel_type_product_codes", "product_code", 32)]
    public async Task The_Column_Is_A_Bounded_Varchar(string table, string column, int expectedLength)
    {
        await using var context = CreateContext();

        var actual = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value"
                FROM information_schema.columns
                WHERE table_name = {0} AND column_name = {1}
                """, table, column)
            .ToListAsync();

        actual.Should().ContainSingle()
            .Which.Should().Be("character varying", "the schema's convention is varchar(n) everywhere");

        var length = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT character_maximum_length AS "Value"
                FROM information_schema.columns
                WHERE table_name = {0} AND column_name = {1}
                """, table, column)
            .ToListAsync();

        length.Should().ContainSingle()
            .Which.Should().Be(expectedLength,
                "the bound is the one the CHECK constraint beside the column already states");
    }

    [Fact]
    public async Task The_Check_Constraints_Are_Still_There()
    {
        // The type change rebuilds the constraints that depend on the column, and a rebuild is
        // where a constraint can quietly disappear.
        await using var context = CreateContext();

        var constraints = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT con.conname AS "Value"
                FROM pg_constraint con
                JOIN pg_class rel ON rel.oid = con.conrelid
                WHERE con.conname IN (
                    'ck_fuel_nominals_data_kind',
                    'ck_fuel_types_name_length',
                    'ck_fuel_types_brand_fuel_code_length',
                    'ck_fuel_type_product_codes_product_code_length')
                """)
            .ToListAsync();

        constraints.Should().HaveCount(4);
    }
}
