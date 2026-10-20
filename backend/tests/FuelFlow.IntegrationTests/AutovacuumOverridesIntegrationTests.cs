using FluentAssertions;
using FuelFlow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The tables that are churned row by row get their own autovacuum thresholds.
/// </summary>
/// <remarks>
/// <para>
/// Every table ran on the cluster's defaults: <c>autovacuum_vacuum_scale_factor = 0.2</c> and
/// <c>autovacuum_analyze_scale_factor = 0.1</c>. On a table that is imported in bulk and then
/// updated row by row, that means the table carries a fifth of its size in dead rows before the
/// first vacuum wakes up for it.
/// </para>
/// <para>
/// Measured on a replica built from production's own dump and filled to 500 000 vouchers, after
/// the 100 000 row updates the fulfilment and expiry jobs produce: the heap grew 19 %, the indexes
/// 15 %, and 19.4 % of the table was dead tuples. The live box shows the same shape in
/// miniature - <c>refresh_tokens</c> (871 live, 66 dead) and <c>provider_event_outbox</c> (74 live,
/// 35 dead) have never been vacuumed at all, because 66 dead rows is under the threshold a 0.2
/// scale factor sets.
/// </para>
/// <para>
/// <b>Why the assertion is against the database and not the model.</b> EF has no representation for
/// a table's storage options, so the model cannot say anything about this. The setting is a
/// property of the TABLE, and the table is what has to carry it.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class AutovacuumOverridesIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AutovacuumOverridesIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task The_Tables_That_Are_Churned_Row_By_Row_Get_Their_Own_Thresholds()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname || '|' || c.reloptions::text
              FROM pg_class c
              JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = current_schema()
             WHERE c.relname IN ('fuel_vouchers', 'orders')
             ORDER BY c.relname
            """;

        var options = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = reader.GetString(0);
            var parts = row.Split('|', 2);
            options[parts[0]] = parts.Length > 1 ? parts[1] : "";
        }

        options.Should().HaveCount(2, "both tables are read");

        options["fuel_vouchers"].Should().Contain("autovacuum_vacuum_scale_factor=0.02",
            "the voucher table is imported in bulk and then updated row by row, so a fifth of it in dead rows is too much to carry");
        options["fuel_vouchers"].Should().Contain("autovacuum_analyze_scale_factor=0.01");

        options["orders"].Should().Contain("autovacuum_vacuum_scale_factor=0.05",
            "the order table moves through its statuses the same way, and a quarter of the default threshold is enough");
        options["orders"].Should().Contain("autovacuum_analyze_scale_factor=0.02");
    }

    [Fact]
    public async Task A_Table_That_Is_Not_Churned_Keeps_The_Cluster_Defaults()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.reloptions IS NULL
              FROM pg_class c
              JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = current_schema()
             WHERE c.relname = 'brands'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetBoolean(0).Should().BeTrue(
            "the brands table is four rows and changes weekly; a setting on it would cost nothing and buy nothing");
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
