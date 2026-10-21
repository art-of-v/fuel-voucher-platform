using FluentAssertions;
using FuelFlow.Persistence;
using FuelFlow.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The safety net reads the outbox through an index of its own.
/// </summary>
/// <remarks>
/// <para>
/// Three jobs read the outbox every five minutes - the fulfilment service and the two
/// notification passes - and each asks for the events of its own type that are still
/// unprocessed and older than the safety-net window. Five of the eight event types have no
/// consumer and are never marked processed: they are the record of something that happened,
/// and they are most of the table.
/// </para>
/// <para>
/// Before the index, the plan for that query was an index scan over <c>created_at_utc</c> that
/// walked the whole thirty-day window, one row at a time, and filtered the two other
/// predicates off the heap. Measured on a table of 200 000 rows spread over thirty days, in
/// the shape the table really has: <b>199 951 rows visited, 0 found, 37.0 ms</b>. With the
/// index: <b>5 rows visited, 0 found, 0.034 ms</b>.
/// </para>
/// <para>
/// <b>Why a test and not a comment.</b> A planner that stops choosing the index is a
/// performance regression that nothing else would notice: the query still answers, still
/// returns the right events, and gets slower every month as the table grows. The schema
/// assertion says the index is there; the plan assertion says it is USED.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class TheSafetyNetHasItsOwnIndex : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TheSafetyNetHasItsOwnIndex(TestDatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task The_Index_Exists_And_Names_Exactly_The_Types_That_Have_A_Consumer()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_get_indexdef(indexrelid)
              FROM pg_index
             WHERE indrelid = 'outbox_events'::regclass
               AND indexrelid::regclass::text = 'ix_outbox_events_pending_consumed'
            """;

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull("the safety net reads the outbox through this index");

        // The filter is what makes the index small, and it names the types that have a
        // consumer. Every one of them has to be there, and nothing else may be: a type that
        // nobody reads in the list makes the index hold rows the safety net never wants.
        foreach (var consumed in Features.Orders.SharedModels.ConsumedOutboxEventTypes.All)
        {
            definition.Should().Contain($"'{consumed}'", "a consumed type that is not in the index keeps the old plan");
        }

        definition.Should().NotContain("'OrderCancelled'",
            "a type with no consumer in the index makes it hold the audit trail");
        definition.Should().NotContain("'PaymentCompleted'",
            "a type with no consumer in the index makes it hold the audit trail");
        definition.Should().Contain("processed = false",
            "the index holds the events that are waiting, not the whole table");
    }

    [Fact]
    public async Task The_Dispatchers_Query_Uses_It()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        // The table as it is in production's shape: thirty days of events, five of the eight
        // types never consumed, and the consumed ones processed within seconds.
        //
        // A raw command, not ExecuteSqlRaw: that one runs the SQL through string.Format, and
        // "i % 8" in a modulo is a format specifier to it.
        await using var transaction = await context.Database.BeginTransactionAsync();
        var insert = context.Database.GetDbConnection().CreateCommand();
        insert.CommandText = """
            WITH base AS (
                SELECT
                    i,
                    (ARRAY['OrderCreated','OrderFulfilled','OrderCancelled','PaymentCompleted','VoucherExpired','VoucherUsed','VoucherActivated','VoucherIssued'])[1 + (i % 8)] AS event_type,
                    (i % 8) IN (0, 7) AS consumed,
                    now() - (interval '1 minute' * (i % 43200)) AS created_at
                FROM generate_series(1, 60000) AS i
            )
            INSERT INTO outbox_events (id, event_type, payload, processed, processed_at_utc, created_at_utc)
            SELECT
                gen_random_uuid(),
                event_type,
                '{}',
                CASE WHEN event_type = 'OrderFulfilled' THEN created_at < now() - interval '10 minutes' ELSE consumed END,
                CASE WHEN event_type = 'OrderFulfilled' THEN NULL
                     WHEN consumed THEN now() - interval '1 minute' ELSE NULL END,
                created_at
            FROM base
            """;
        await insert.ExecuteNonQueryAsync();
        await context.Database.ExecuteSqlRawAsync("ANALYZE outbox_events");

        var sql = context.OutboxEvents
            .Where(e => !e.Processed
                     && e.EventType == Features.Orders.SharedModels.OutboxEventType.OrderFulfilled
                     && e.CreatedAtUtc < TestClock.UtcNow.AddMinutes(-10))
            .OrderBy(e => e.CreatedAtUtc)
            .Take(50)
            .ToQueryString();

        var plan = await ExplainAsync(context, sql);

        _output.WriteLine(plan);

        plan.Should().Contain("ix_outbox_events_pending_consumed",
            "the safety net has to read the events that are waiting, not walk the audit trail to find them");

        await transaction.RollbackAsync();
    }

    private async Task<string> ExplainAsync(ApplicationDbContext context, string sql)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, COSTS) " + sql;

        var cutoff = command.CreateParameter();
        cutoff.ParameterName = "AddMinutes";
        cutoff.Value = TestClock.UtcNow.AddMinutes(-10);
        command.Parameters.Add(cutoff);

        var limit = command.CreateParameter();
        limit.ParameterName = "p";
        limit.Value = 50;
        command.Parameters.Add(limit);

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);
}
