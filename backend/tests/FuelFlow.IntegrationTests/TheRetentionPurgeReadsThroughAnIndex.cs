using System.Text.RegularExpressions;
using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The nightly retention purge reads the outbox through an index.
/// </summary>
/// <remarks>
/// <para>
/// The purge removes two sets of rows, aged by two different columns: the events that were
/// published, aged by when they were published, and the events nobody will ever read again -
/// the types with no consumer - aged by when they happened.
/// </para>
/// <para>
/// Neither half had an index. The only index that led <c>created_at_utc</c> was the PARTIAL one
/// the dispatchers' safety net reads, and its filter is <c>processed = false AND event_type IN
/// (the consumed types)</c> - the opposite set from the purge's second half, which asks for the
/// events whose type is NOT consumed. A partial index cannot serve a predicate that contradicts
/// its filter, so the purge read the whole table: about two hundred thousand rows, every night,
/// to delete a few thousand.
/// </para>
/// <para>
/// <b>Why a test and not a comment.</b> A planner that stops choosing the index is a performance
/// regression nothing else would notice: the purge still deletes the right rows, and gets slower
/// every month as the table grows. The schema assertion says the index is there; the plan
/// assertion says it is USED.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheRetentionPurgeReadsThroughAnIndex : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TheRetentionPurgeReadsThroughAnIndex(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// The two indexes the purge reads through, named by what they lead.
    /// </summary>
    [Fact]
    public async Task The_Purge_Has_An_Index_For_Each_Of_Its_Two_Aging_Columns()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname || ' ' || indexdef
              FROM pg_indexes
             WHERE schemaname = current_schema()
               AND tablename = 'outbox_events'
             ORDER BY indexname
            """;

        var definitions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            definitions.Add(reader.GetString(0));
        }

        var all = string.Join(Environment.NewLine, definitions);

        all.Should().Contain("ix_outbox_events_created_at_utc_id",
            "the purge ages the events nobody consumes by when they happened, and nothing led that column");
        all.Should().Contain("ix_outbox_events_processed_at_utc",
            "the purge ages the published events by when they were published, and nothing led that column");
        all.Should().Contain("ix_outbox_events_pending_consumed",
            "the dispatchers' safety net keeps its own index");
    }

    [Fact]
    public async Task The_Purges_Queries_Use_Them()
    {
        var cutoff = TestClock.UtcNow.AddDays(-DataRetentionService.OutboxEventRetentionDays);

        await using (var context = CreateContext())
        {
            await context.Database.MigrateAsync();
            await SeedTheTableInItsRealShapeAsync(context, cutoff);
        }

        await using var explainContext = CreateContext();
        await explainContext.Database.OpenConnectionAsync();

        // The count the dry run sends every night BY DEFAULT: the retention job does not delete
        // until somebody turns it on, so this is the statement the box executes tonight. The count
        // carries the same predicate as the select and no LIMIT, so the plan of this one is the
        // plan of the count.
        var countPlan = await ExplainAsync(explainContext,
            DataRetentionService.UnconsumedOutboxEventsToPurge(explainContext.OutboxEvents, cutoff)
                .ToQueryString(),
            cutoff);

        _output.WriteLine(countPlan);

        countPlan.Should().Contain("ix_outbox_events_created_at_utc_id",
            "the purge counts the old rows through an index, not by reading the whole table");
        countPlan.Should().NotContain("Seq Scan on outbox_events",
            "the purge reads the whole table every night, and that is a scan of the busiest write path in the platform");

        // And the delete itself: a batch of a thousand rows, which is what the job actually loads.
        var batchPlan = await ExplainAsync(explainContext,
            DataRetentionService.UnconsumedOutboxEventsToPurge(explainContext.OutboxEvents, cutoff)
                .AsTracking().Take(1000).ToQueryString(),
            cutoff);

        _output.WriteLine(batchPlan);

        batchPlan.Should().Contain("ix_outbox_events_created_at_utc_id",
            "the batched delete reads the old rows through an index too");
        batchPlan.Should().NotContain("Seq Scan on outbox_events",
            "the batched delete reads the whole table every night");

        var publishedPlan = await ExplainAsync(explainContext,
            DataRetentionService.ProcessedOutboxEventsToPurge(explainContext.OutboxEvents, cutoff)
                .AsTracking().Take(1000).ToQueryString(),
            cutoff);

        _output.WriteLine(publishedPlan);

        publishedPlan.Should().Contain("ix_outbox_events_processed_at_utc",
            "the published half of the purge is aged by processed_at_utc, and nothing else leads that column");
        publishedPlan.Should().NotContain("Seq Scan on outbox_events",
            "the published half of the purge reads the whole table every night");
    }

    /// <summary>
    /// The table in the shape it really has: thirty days of events, five of the eight types never
    /// consumed, and the consumed ones published as soon as they were written.
    /// </summary>
    /// <remarks>
    /// The dates are counted from the purge's own cutoff rather than from the database's clock, so
    /// the shape does not depend on when the suite runs: every row is NEWER than the cutoff,
    /// which is the steady state - the table keeps thirty days and the purge's window is thirty
    /// days, so in production there is nothing older than the cutoff to find.
    /// <para>
    /// The rows are left behind on purpose. The class has a database of its own, and a plan
    /// measured against a table the statistics describe and the table does not hold would be
    /// measuring nothing.
    /// </para>
    /// </remarks>
    private static async Task SeedTheTableInItsRealShapeAsync(ApplicationDbContext context, DateTime cutoff)
    {
        await context.Database.OpenConnectionAsync();

        var insert = context.Database.GetDbConnection().CreateCommand();
        insert.CommandText = """
            WITH base AS (
                SELECT
                    i,
                    (ARRAY['OrderCreated','OrderFulfilled','OrderCancelled','PaymentCompleted','VoucherExpired','VoucherUsed','VoucherActivated','VoucherIssued'])[1 + (i % 8)] AS event_type,
                    (i % 8) IN (0, 7) AS consumed,
                    @cutoff + (interval '1 minute' * (1 + (i % 43200))) AS created_at
                FROM generate_series(1, 60000) AS i
            )
            INSERT INTO outbox_events (id, event_type, payload, processed, processed_at_utc, created_at_utc)
            SELECT
                gen_random_uuid(),
                event_type,
                '{}',
                consumed,
                CASE WHEN consumed THEN created_at ELSE NULL END,
                created_at
            FROM base
            """;

        var seedCutoff = insert.CreateParameter();
        seedCutoff.ParameterName = "cutoff";
        seedCutoff.Value = cutoff;
        insert.Parameters.Add(seedCutoff);

        await insert.ExecuteNonQueryAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE outbox_events");
    }

    private async Task<string> ExplainAsync(ApplicationDbContext context, string sql, DateTime cutoff)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, COSTS) " + sql;

        // The parameters are the ones the query itself carries - the cutoff (twice in the second
        // query: once for the age, once for the "not already aged out by its published time" half)
        // and the batch size. Their names come from EF, so they are read out of the SQL rather
        // than hard-coded: a name that changes would otherwise fail the test for the wrong reason.
        foreach (var name in ParameterNames(sql).Distinct())
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = name.Contains("cutoff", StringComparison.OrdinalIgnoreCase)
                ? cutoff
                : 1000;
            command.Parameters.Add(parameter);
        }

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> ParameterNames(string sql)
        => Regex.Matches(sql, @"@(\w+)").Select(m => m.Groups[1].Value);

    /// <summary>
    /// A context on this class's own database.
    /// </summary>
    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
