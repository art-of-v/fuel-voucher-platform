using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The admin's two default lists sort on a column, and the sort has to be an index scan.
/// </summary>
/// <remarks>
/// <para>
/// The admin's DEFAULT voucher list sorts by <c>created_at_utc DESC, id</c>, and the customers
/// list by <c>users.created_at_utc DESC, id</c>. Neither column led an index, so both sorts were a
/// scan of the whole table.
/// </para>
/// <para>
/// Measured on a replica built from production's own dump and filled to 500 000 vouchers: the
/// voucher list went from 52.8 ms to 0.154 ms, and the filtered variant from 49.3 ms to
/// 0.25 ms. A paged list still has to SORT - the page is the last fifty of the whole table - so
/// paging the endpoint did not fix this and did not make it less true.
/// </para>
/// <para>
/// <b>Why this is asserted against pg_indexes and not the model.</b> The point is what the planner
/// can use, and the property it needs is that the column LEADS an index. Naming the indexes instead
/// would make the test a hostage to the naming convention.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AdminListSortIndexIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AdminListSortIndexIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Columns_The_Admin_Lists_Sort_On_Lead_An_Index()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT want.tablename || '.' || want.col
              FROM (VALUES ('fuel_vouchers', 'created_at_utc'),
                           ('users', 'created_at_utc')) AS want(tablename, col)
              JOIN pg_class t ON t.relname = want.tablename
              JOIN pg_namespace n ON n.oid = t.relnamespace AND n.nspname = current_schema()
              JOIN pg_attribute a ON a.attrelid = t.oid AND a.attname = want.col
             WHERE EXISTS (
                   SELECT 1
                     FROM pg_index i
                    WHERE i.indrelid = t.oid
                      AND i.indkey[0] = a.attnum
                 )
            """;

        var present = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            present.Add(reader.GetString(0));
        }

        present.Should().BeEquivalentTo(new[]
        {
            "fuel_vouchers.created_at_utc",
            "users.created_at_utc"
        }, "a list that sorts on a column with no index behind it sorts the whole table");
    }

    [Fact]
    public async Task The_Sort_Indexes_Are_Descending_On_The_Time_And_Ascending_On_The_Id()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexdef
              FROM pg_indexes
             WHERE schemaname = current_schema()
               AND indexname IN ('ix_fuel_vouchers_created_at_utc_id', 'ix_users_created_at_utc_id')
             ORDER BY indexname
            """;

        var definitions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            definitions.Add(reader.GetString(0));
        }

        definitions.Should().HaveCount(2, "both indexes are installed");
        definitions.Should().OnlyContain(d => d.Contains("created_at_utc DESC, id)"),
            "the sort is DESC on the time and ASC on the id, so the order is total");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
