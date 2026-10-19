using FluentAssertions;
using FuelFlow.API.BackgroundJobs;
using FuelFlow.Features.Settings;
using FuelFlow.Features.Settings.SharedModels;
using FuelFlow.Persistence;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Covers the nightly <see cref="HangfireHistoryCleanupService"/> against a real PostgreSQL
/// container: it removes the finished jobs Hangfire's own expiration manager should have taken and
/// did not, and leaves every other row alone — including a job that is queued, being processed, or
/// waiting to be retried.
/// </summary>
/// <remarks>
/// The <c>hangfire</c> schema is not part of the EF model, so the fixtures write to it directly.
/// It is created by Hangfire's own storage — the same installer that runs in production — rather
/// than by booting the API, so that no Hangfire worker is running while the fixtures are in place:
/// a worker would dequeue an "enqueued" fixture and move it out from under the assertion.
/// </remarks>
[Collection("Integration Tests")]
public sealed class HangfireHistoryCleanupIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HangfireHistoryCleanupIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Cleanup_Takes_The_Jobs_The_Expiration_Manager_Missed_And_Leaves_The_Rest()
    {
        await EnsureHangfireSchemaAsync();

        var now = DateTime.UtcNow;
        long missed, missedState, missedParameter;
        long expiredButInGrace, recent, oldWithoutExpiry;
        long oldFailed, oldScheduled, oldEnqueued, oldProcessing;

        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            // Finished a day and a bit ago, so its expiry stamp went past three hours ago: the
            // manager runs hourly and should have taken this. It is the backstop's case.
            (missed, missedState, missedParameter) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: now.AddHours(-27), stateCreatedAt: now.AddHours(-27),
                expireAt: now.AddHours(-(HangfireHistoryCleanupService.ReaperGraceHours + 1)));

            // Expired an hour ago, which is inside the grace: the manager has not had its pass
            // yet, so this job must not race it.
            (expiredButInGrace, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: now.AddHours(-25), stateCreatedAt: now.AddHours(-25),
                expireAt: now.AddHours(-(HangfireHistoryCleanupService.ReaperGraceHours - 1)));

            // Finished two hours ago: nothing about it is due.
            (recent, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: now.AddHours(-2), stateCreatedAt: now.AddHours(-2), expireAt: now.AddHours(22));

            // Finished long ago and never stamped: the manager would never notice this one, so the
            // hard bound is the only thing that reaches it.
            (oldWithoutExpiry, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);

            // Not finished: never eligible, however old.
            (oldFailed, _, _) = await SeedJobAsync(
                connection, transaction, "Failed",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);
            (oldScheduled, _, _) = await SeedJobAsync(
                connection, transaction, "Scheduled",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);
            (oldEnqueued, _, _) = await SeedJobAsync(
                connection, transaction, "Enqueued",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);
            (oldProcessing, _, _) = await SeedJobAsync(
                connection, transaction, "Processing",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);

            await transaction.CommitAsync();
        }

        await RunAsync();

        // The jobs the manager missed are gone, and their history went with them — the state and
        // parameter rows cascade from the job.
        (await JobExistsAsync(missed)).Should().BeFalse("its expiry went past hours ago and the manager never took it");
        (await StateExistsAsync(missedState)).Should().BeFalse("the state row cascades from the job");
        (await ParameterExistsAsync(missedParameter)).Should().BeFalse("the parameter row cascades from the job");
        (await JobExistsAsync(oldWithoutExpiry)).Should().BeFalse("nothing would ever have noticed this one but the bound");

        // Everything else is still there.
        (await JobExistsAsync(expiredButInGrace)).Should().BeTrue("the manager has not had its pass yet; the job must not race it");
        (await JobExistsAsync(recent)).Should().BeTrue("it finished two hours ago");
        (await JobExistsAsync(oldFailed)).Should().BeTrue("a failed job is what the dashboard's retry button needs");
        (await JobExistsAsync(oldScheduled)).Should().BeTrue("a scheduled job has not run yet");
        (await JobExistsAsync(oldEnqueued)).Should().BeTrue("an enqueued job has not been picked up");
        (await JobExistsAsync(oldProcessing)).Should().BeTrue("a processing job is in flight right now");
    }

    [Fact]
    public async Task Cleanup_Removes_The_Queue_Row_Of_A_Deleted_Job_And_Leaves_The_Other_One()
    {
        await EnsureHangfireSchemaAsync();

        long deletedJob, keptJob, keptQueueRow, orphanQueueRow;
        var now = DateTime.UtcNow;

        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            (deletedJob, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: now.AddDays(-3), stateCreatedAt: now.AddDays(-3), expireAt: now.AddDays(-2));
            (keptJob, _, _) = await SeedJobAsync(
                connection, transaction, "Enqueued",
                createdAt: now.AddDays(-40), stateCreatedAt: now.AddDays(-40), expireAt: null);

            // A queue row that points at a job which is not there: the one table in Hangfire's schema
            // with no foreign key onto hangfire.job, so a deleted job leaves its queue entry behind.
            orphanQueueRow = await InsertQueueRowAsync(connection, transaction, jobId: 999_999_999);
            keptQueueRow = await InsertQueueRowAsync(connection, transaction, jobId: keptJob);

            await transaction.CommitAsync();
        }

        await RunAsync();

        (await JobExistsAsync(deletedJob)).Should().BeFalse();
        (await QueueRowExistsAsync(keptQueueRow)).Should().BeTrue("the job it points at is still there");
        (await QueueRowExistsAsync(orphanQueueRow)).Should().BeFalse("its job is gone, so the row is garbage");
    }

    [Fact]
    public async Task Cleanup_WhenDisabled_Deletes_Nothing()
    {
        await EnsureHangfireSchemaAsync();

        await SetSettingAsync(AppSettingKeys.HangfireCleanupEnabled, "false");

        long job;
        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            (job, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: DateTime.UtcNow.AddDays(-3), stateCreatedAt: DateTime.UtcNow.AddDays(-3), expireAt: DateTime.UtcNow.AddDays(-2));
            await transaction.CommitAsync();
        }

        await RunAsync();

        (await JobExistsAsync(job)).Should().BeTrue("the switch is off, so the job deletes nothing");
    }

    [Fact]
    public async Task Cleanup_Honours_The_Hard_Bound()
    {
        await EnsureHangfireSchemaAsync();

        await SetSettingAsync(AppSettingKeys.HangfireCleanupRetentionDays, "30");

        // Both are unstamped, so only the bound can reach them.
        long tenDaysOld, fortyDaysOld;
        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            (tenDaysOld, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: DateTime.UtcNow.AddDays(-10), stateCreatedAt: DateTime.UtcNow.AddDays(-10), expireAt: null);
            (fortyDaysOld, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: DateTime.UtcNow.AddDays(-40), stateCreatedAt: DateTime.UtcNow.AddDays(-40), expireAt: null);
            await transaction.CommitAsync();
        }

        await RunAsync();

        (await JobExistsAsync(tenDaysOld)).Should().BeTrue("10 days is inside a 30-day bound");
        (await JobExistsAsync(fortyDaysOld)).Should().BeFalse("40 days is outside a 30-day bound");
    }

    /// <summary>
    /// A job that finished long ago but whose expiry stamp is still in the future is not eligible:
    /// the stamp is the manager's contract, and a row that has not expired yet is a row the manager
    /// has not been asked to take. This is the case that separates "the job finished" from "the job
    /// is due", and it is the one a naive age-based predicate gets wrong.
    /// </summary>
    [Fact]
    public async Task Cleanup_Leaves_A_Finished_Job_Whose_Expiry_Has_Not_Come()
    {
        await EnsureHangfireSchemaAsync();

        long job;
        await using (var connection = await OpenAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            (job, _, _) = await SeedJobAsync(
                connection, transaction, "Succeeded",
                createdAt: DateTime.UtcNow.AddDays(-2), stateCreatedAt: DateTime.UtcNow.AddDays(-2), expireAt: DateTime.UtcNow.AddDays(1));
            await transaction.CommitAsync();
        }

        await RunAsync();

        (await JobExistsAsync(job)).Should().BeTrue("its expiry is still in the future, so nothing is due yet");
    }

    private async Task SetSettingAsync(string key, string value)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO app_settings (key, value, updated_at_utc)
            VALUES ({0}, {1}, NOW())
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at_utc = NOW()
            """,
            key,
            value);
    }

    private async Task RemoveSettingAsync(string key)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM app_settings WHERE key = {0}", key);
    }

    private async Task RunAsync()
    {
        await using var dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).Build();
        await using var context = CreateContext();
        var service = new HangfireHistoryCleanupService(
            dataSource,
            new RuntimeSettingsService(context),
            NullLogger<HangfireHistoryCleanupService>.Instance);

        await service.CleanupAsync();
    }

    /// <summary>
    /// Creates the <c>hangfire</c> schema with Hangfire's own installer — the same code path that
    /// creates it on a production boot — and clears the switches a previous test in this class may
    /// have left behind. The default is on and a week, and a leftover row would silently change
    /// what the next test asserts.
    /// </summary>
    private async Task EnsureHangfireSchemaAsync()
    {
        // The storage is not disposable and holds no connection of its own; constructing it is what
        // installs the schema.
        _ = new PostgreSqlStorage(_fixture.ConnectionString);

        await RemoveSettingAsync(AppSettingKeys.HangfireCleanupEnabled);
        await RemoveSettingAsync(AppSettingKeys.HangfireCleanupRetentionDays);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private ApplicationDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options, TestQrCipher.Instance);

    /// <summary>
    /// Writes one job the way Hangfire writes it: a job row, the state row it points at, and a
    /// parameter row. The state's own timestamp is the moment the job reached that state, which is
    /// what the cleanup ages the history by.
    /// </summary>
    private static async Task<(long JobId, long StateId, long ParameterId)> SeedJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string stateName,
        DateTime createdAt,
        DateTime stateCreatedAt,
        DateTime? expireAt)
    {
        long jobId;
        await using (var insertJob = new NpgsqlCommand(
            """
            INSERT INTO hangfire.job (statename, invocationdata, arguments, createdat, expireat)
            VALUES (@statename, '{}'::jsonb, '[]'::jsonb, @createdat, @expireat)
            RETURNING id
            """,
            connection,
            transaction))
        {
            insertJob.Parameters.Add(new NpgsqlParameter("statename", NpgsqlDbType.Text) { Value = stateName });
            insertJob.Parameters.Add(new NpgsqlParameter("createdat", NpgsqlDbType.TimestampTz) { Value = createdAt });
            insertJob.Parameters.Add(new NpgsqlParameter("expireat", NpgsqlDbType.TimestampTz)
            {
                Value = expireAt is { } value ? value : DBNull.Value
            });

            jobId = (long)(await insertJob.ExecuteScalarAsync())!;
        }

        long stateId;
        await using (var insertState = new NpgsqlCommand(
            """
            INSERT INTO hangfire.state (jobid, name, createdat)
            VALUES (@jobid, @name, @createdat)
            RETURNING id
            """,
            connection,
            transaction))
        {
            insertState.Parameters.Add(new NpgsqlParameter("jobid", NpgsqlDbType.Bigint) { Value = jobId });
            insertState.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = stateName });
            insertState.Parameters.Add(new NpgsqlParameter("createdat", NpgsqlDbType.TimestampTz) { Value = stateCreatedAt });

            stateId = (long)(await insertState.ExecuteScalarAsync())!;
        }

        await using (var linkState = new NpgsqlCommand(
            "UPDATE hangfire.job SET stateid = @stateid WHERE id = @jobid",
            connection,
            transaction))
        {
            linkState.Parameters.Add(new NpgsqlParameter("stateid", NpgsqlDbType.Bigint) { Value = stateId });
            linkState.Parameters.Add(new NpgsqlParameter("jobid", NpgsqlDbType.Bigint) { Value = jobId });
            await linkState.ExecuteNonQueryAsync();
        }

        long parameterId;
        await using (var insertParameter = new NpgsqlCommand(
            """
            INSERT INTO hangfire.jobparameter (jobid, name, value)
            VALUES (@jobid, 'Test', 'value')
            RETURNING id
            """,
            connection,
            transaction))
        {
            insertParameter.Parameters.Add(new NpgsqlParameter("jobid", NpgsqlDbType.Bigint) { Value = jobId });
            parameterId = (long)(await insertParameter.ExecuteScalarAsync())!;
        }

        return (jobId, stateId, parameterId);
    }

    private static async Task<long> InsertQueueRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long jobId)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO hangfire.jobqueue (jobid, queue, fetchedat)
            VALUES (@jobid, 'default', NULL)
            RETURNING id
            """,
            connection,
            transaction);

        command.Parameters.Add(new NpgsqlParameter("jobid", NpgsqlDbType.Bigint) { Value = jobId });
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<bool> JobExistsAsync(long jobId) => await ExistsAsync("hangfire.job", "id", jobId);

    private async Task<bool> StateExistsAsync(long stateId) => await ExistsAsync("hangfire.state", "id", stateId);

    private async Task<bool> ParameterExistsAsync(long parameterId) => await ExistsAsync("hangfire.jobparameter", "id", parameterId);

    private async Task<bool> QueueRowExistsAsync(long queueRowId) => await ExistsAsync("hangfire.jobqueue", "id", queueRowId);

    private async Task<bool> ExistsAsync(string table, string column, long id)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE {column} = @id", connection);
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = id });

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
