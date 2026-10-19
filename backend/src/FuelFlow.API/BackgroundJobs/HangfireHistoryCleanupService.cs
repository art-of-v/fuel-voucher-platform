using FuelFlow.Features.Settings;
using Npgsql;
using NpgsqlTypes;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Nightly backstop for Hangfire's own expiration manager, and the alarm that says whether it is
/// still working.
/// </summary>
/// <remarks>
/// <para>
/// <b>What Hangfire already does.</b> The PostgreSQL storage ships an expiration manager: when a
/// job reaches a final state, Hangfire stamps <c>expireat</c> on its row, and the manager deletes
/// the row once that stamp is in the past. Measured on production on 2026-10-19, that works — all
/// 6,896 job rows carried an expiry, the stamps spanned exactly one day, and the <c>hangfire</c>
/// schema sat at a steady 14 MB rather than growing. So this job is <em>not</em> a cleanup the
/// system was missing.
/// </para>
/// <para>
/// <b>Why it exists anyway.</b> The manager is a third party's background process, and nothing
/// notices when it stops. The failure is silent — no error, no failed job, no alert — and the
/// symptom only appears much later as a table that has quietly grown by tens of thousands of rows.
/// That is exactly how the first version of this problem was misdiagnosed: a 14 MB
/// <c>hangfire</c> schema was read as an unbounded growth curve, and the fix that followed —
/// recreating the schema in a maintenance window — would have been spent on a working reaper.
/// </para>
/// <para>
/// <b>What it deletes.</b> Two things, and only two. A finished job whose own expiry stamp went
/// past more than <see cref="ReaperGraceHours"/> ago — which is to say, a job the manager was
/// supposed to take and did not — and a finished job that finished more than the retention window
/// ago, which is the hard bound for the case where the stamp was never written at all. Nothing
/// else is ever matched: a job that is queued, being processed, waiting to be retried or waiting on
/// a continuation is not finished, and a finished job cannot become unfinished again. A job that is
/// claimed but not yet executed therefore cannot be removed from under the worker.
/// </para>
/// <para>
/// <b>What it leaves alone.</b> A failed job. Hangfire treats <c>Failed</c> as a non-final state
/// precisely so that the failure stays on the dashboard and can be retried by hand, and this job
/// does not take that away.
/// </para>
/// <para>
/// <b>What it is not.</b> A business record. Every row it removes is the scheduler's own note about
/// work that already completed — there is no money, no customer and no settlement in any of them.
/// </para>
/// </remarks>
public class HangfireHistoryCleanupService
{
    /// <summary>
    /// How long a finished job may sit past its own expiry stamp before this job takes it.
    /// <para>
    /// Hangfire's expiration manager runs once an hour, so two hours means the manager has had two
    /// passes and missed the row. A shorter grace would race the manager and delete rows it was
    /// about to delete itself — harmless, but it would make the "the manager is keeping up" number
    /// in the log meaningless.
    /// </para>
    /// </summary>
    public const int ReaperGraceHours = 2;

    /// <summary>
    /// Hangfire's storage schema. Not configurable, because Hangfire's own
    /// <c>PostgreSqlStorageOptions.SchemaName</c> is not either: the app leaves it at the default,
    /// and <c>deploy/rebuild-collation.sh</c> already refers to <c>hangfire.job</c> by the same
    /// spelling.
    /// </summary>
    private const string SchemaName = "hangfire";

    /// <summary>
    /// Bounds a single run to one cheap transaction; a larger backlog drains over successive nightly
    /// runs rather than one unbounded delete holding locks and bloating WAL.
    /// </summary>
    private const int BatchSize = 1000;

    private readonly NpgsqlDataSource _dataSource;
    private readonly RuntimeSettingsService _settings;
    private readonly ILogger<HangfireHistoryCleanupService> _logger;

    public HangfireHistoryCleanupService(
        NpgsqlDataSource dataSource,
        RuntimeSettingsService settings,
        ILogger<HangfireHistoryCleanupService> logger)
    {
        _dataSource = dataSource;
        _settings = settings;
        _logger = logger;
    }

    public virtual async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (!await _settings.IsHangfireCleanupEnabledAsync(cancellationToken))
        {
            _logger.LogDebug("Hangfire history cleanup skipped (HangfireCleanup:Enabled is off)");
            return;
        }

        var retentionDays = Math.Max(1, await _settings.GetHangfireCleanupRetentionDaysAsync(cancellationToken));
        var now = DateTime.UtcNow;
        var missedBefore = now.AddHours(-ReaperGraceHours);
        var cutoff = now.AddDays(-retentionDays);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // What the table holds before the purge, and — the number that matters — how many of those
        // rows Hangfire's own expiration manager ought already to have taken. A non-zero "missed by
        // the manager" count means the manager is not running, which is the fact this job exists to
        // work around; the log is where an operator will look for it.
        _logger.LogInformation(
            "Hangfire job history before cleanup: {Breakdown}.",
            await ReadJobStateBreakdownAsync(connection, missedBefore, cancellationToken));

        var deletedJobs = await DeleteMissedJobsAsync(connection, missedBefore, cutoff, cancellationToken);
        var deletedQueueRows = await DeleteOrphanQueueRowsAsync(connection, cancellationToken);

        if (deletedJobs == 0)
        {
            _logger.LogInformation(
                "Hangfire history cleanup: nothing to remove. Hangfire's own expiration manager is "
                + "keeping up with the finished jobs, and the table stays at its one-day steady state.");
            return;
        }

        // This is an anomaly, not a routine: the manager was supposed to take these rows, and did
        // not. It is a warning rather than an error on purpose — nothing is broken for the user, and
        // the admin's error journal only carries errors and fatals.
        _logger.LogWarning(
            "Hangfire's own expiration manager left {JobCount} finished job(s) behind, and they were "
            + "removed here. The table is bounded again, but the manager is not doing its job: the "
            + "grace is {GraceHours}h and the hard bound is {RetentionDays}d.",
            deletedJobs, ReaperGraceHours, retentionDays);
    }

    /// <summary>
    /// The rows of <c>hangfire.job</c> grouped by state, with the expiry stamp counted separately.
    /// A finished job's stamp is what Hangfire's expiration manager sorts on, so "past expiry" is the
    /// shape of a manager that is running, and "missed by the manager" is the shape of one that is
    /// not.
    /// </summary>
    private async Task<string> ReadJobStateBreakdownAsync(
        NpgsqlConnection connection,
        DateTime missedBefore,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT statename,
                   count(*),
                   count(expireat),
                   count(*) FILTER (WHERE expireat < NOW()),
                   count(*) FILTER (WHERE expireat < @missedBefore)
              FROM hangfire.job
             GROUP BY statename
             ORDER BY statename
            """,
            connection);

        command.Parameters.Add(new NpgsqlParameter("missedBefore", NpgsqlDbType.TimestampTz) { Value = missedBefore });

        var parts = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var state = reader.IsDBNull(0) ? "(none)" : reader.GetString(0);
            parts.Add(
                $"{state}={reader.GetInt64(1)} (expiry set: {reader.GetInt64(2)}, "
                + $"past expiry: {reader.GetInt64(3)}, missed by the manager: {reader.GetInt64(4)})");
        }

        return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
    }

    /// <summary>
    /// Removes the finished jobs the expiration manager should have taken, in bounded batches.
    /// </summary>
    /// <remarks>
    /// The age is read from the job's own current state row rather than from the job's creation
    /// time: a job that was created a week ago and finished an hour ago has just finished, and
    /// deleting it would throw away a fresh record. <c>stateid</c> points at the state the job is in
    /// now, so for a <c>Succeeded</c> job that is the row written when it succeeded. A job whose
    /// state row is missing falls back to its creation time, which is the older of the two and
    /// therefore the safe direction.
    /// </remarks>
    private async Task<int> DeleteMissedJobsAsync(
        NpgsqlConnection connection,
        DateTime missedBefore,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        var deleted = 0;

        while (true)
        {
            var ids = await SelectMissedJobIdsAsync(connection, missedBefore, cutoff, cancellationToken);
            if (ids.Length == 0)
            {
                return deleted;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // hangfire.jobqueue has no foreign key onto hangfire.job — unlike state and jobparameter,
            // which cascade — so the queue rows of a job we delete would outlive it as orphans.
            await DeleteByJobIdsAsync(connection, transaction, "jobqueue", "jobid", ids, cancellationToken);

            // Deleting the job row cascades to its state and jobparameter rows, so the job's history
            // goes with it in one statement.
            var removed = await DeleteByJobIdsAsync(connection, transaction, "job", "id", ids, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            deleted += removed;

            if (removed == 0)
            {
                // Somebody else took these rows between the SELECT and the DELETE — Hangfire's own
                // expiration manager, most likely. There is nothing left to do, and looping on it
                // would spin forever.
                return deleted;
            }
        }
    }

    private async Task<long[]> SelectMissedJobIdsAsync(
        NpgsqlConnection connection,
        DateTime missedBefore,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT j.id
              FROM hangfire.job j
              LEFT JOIN hangfire.state s ON s.id = j.stateid
             WHERE j.statename = 'Succeeded'
               AND (
                     (j.expireat IS NOT NULL AND j.expireat < @missedBefore)
                  OR COALESCE(s.createdat, j.createdat) < @cutoff
                   )
             ORDER BY COALESCE(s.createdat, j.createdat)
             LIMIT @batch
            """,
            connection);

        command.Parameters.Add(new NpgsqlParameter("missedBefore", NpgsqlDbType.TimestampTz) { Value = missedBefore });
        command.Parameters.Add(new NpgsqlParameter("cutoff", NpgsqlDbType.TimestampTz) { Value = cutoff });
        command.Parameters.Add(new NpgsqlParameter("batch", NpgsqlDbType.Integer) { Value = BatchSize });

        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids.ToArray();
    }

    private async Task<int> DeleteByJobIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string column,
        long[] ids,
        CancellationToken cancellationToken)
    {
        // The table and column names are literals from this file, never anything from outside it;
        // the ids are a bound array.
        await using var command = new NpgsqlCommand(
            $"DELETE FROM {SchemaName}.{table} WHERE {column} = ANY(@ids)",
            connection,
            transaction);

        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = ids });

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Queue rows whose job no longer exists. A job that is deleted while it is still queued leaves
    /// its queue row behind, and nothing else ever removes it: <c>hangfire.jobqueue</c> is the one
    /// table in the schema with no foreign key onto <c>hangfire.job</c>.
    /// </summary>
    private async Task<int> DeleteOrphanQueueRowsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM hangfire.jobqueue q
             WHERE NOT EXISTS (SELECT 1 FROM hangfire.job j WHERE j.id = q.jobid)
            """,
            connection);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
