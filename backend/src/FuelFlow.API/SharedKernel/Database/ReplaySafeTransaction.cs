using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace FuelFlow.SharedKernel.Database;

/// <summary>
/// The one way an explicit transaction is opened in this app.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's retrying execution strategy is enabled on the Npgsql provider (see
/// <c>DatabaseSetup.AddDatabase</c>). A strategy refuses a user-initiated transaction: with
/// <c>EnableRetryOnFailure</c> configured, the first save inside a transaction opened by hand
/// throws <c>InvalidOperationException: The configured execution strategy ... does not support
/// user-initiated transactions</c> — at the save, not at the <c>BeginTransaction</c>, so it is a
/// runtime failure on the money paths rather than a compile error.
/// </para>
/// <para>
/// The transaction therefore belongs INSIDE the strategy, which means the body of the transaction
/// belongs inside a delegate the strategy may run again. That is the whole shape of this helper:
/// the operation is a lambda, the transaction opens inside it, and the commit is the strategy's to
/// make.
/// </para>
/// <para>
/// Three things make the replay safe rather than merely possible:
/// </para>
/// <list type="number">
/// <item>
/// The state the retry works from is the state the body started from — the values it changed are
/// undone, and the state it found them in is restored. The body then re-reads what it read the
/// first time and re-applies its change to it. See <see cref="TrackerSnapshot"/>.
/// </item>
/// <item>
/// The entities the body created are detached before the retry, so the body adds them once rather
/// than twice, and two instances of one key are never tracked at once.
/// </item>
/// <item>
/// A commit that fails transiently is ambiguous: the database may have taken it and lost the answer
/// on the way back. <c>verifySucceeded</c> is the question asked in that case — "did the write
/// land?" A site that can answer it passes a delegate; a site that cannot has to accept that a
/// failed commit may be retried and land twice.
/// </item>
/// </list>
/// <para>
/// The body must also not repeat a side effect that happened outside the transaction — a payment, a
/// message, a file, a metrics counter. Those belong after the commit.
/// </para>
/// </remarks>
public static class ReplaySafeTransaction
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction the execution strategy may replay.
    /// </summary>
    /// <param name="context">The context the transaction runs on.</param>
    /// <param name="operation">
    /// The body of the transaction. It runs inside the transaction and may run again if the commit
    /// fails transiently, so it must be safe to run twice.
    /// </param>
    /// <param name="cancellationToken">The token the transaction obeys.</param>
    /// <param name="verifySucceeded">
    /// Asked after a failed commit: did the write land anyway? <c>true</c> ends the transaction as a
    /// success. <c>null</c> means the site cannot answer, and a failed commit is then retried.
    /// </param>
    /// <param name="isolationLevel">The isolation level of the transaction.</param>
    public static Task ExecuteReplaySafeAsync(
        this DbContext context,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<bool>>? verifySucceeded = null,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        => ExecuteReplaySafeAsync<bool>(
            context,
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken,
            verifySucceeded,
            isolationLevel);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction the execution strategy may replay, and
    /// answers with what the operation returned.
    /// </summary>
    /// <typeparam name="TResult">What the operation answers.</typeparam>
    /// <param name="context">The context the transaction runs on.</param>
    /// <param name="operation">
    /// The body of the transaction. It runs inside the transaction and may run again if the commit
    /// fails transiently, so it must be safe to run twice.
    /// </param>
    /// <param name="cancellationToken">The token the transaction obeys.</param>
    /// <param name="verifySucceeded">
    /// Asked after a failed commit: did the write land anyway? <c>true</c> ends the transaction as a
    /// success. <c>null</c> means the site cannot answer, and a failed commit is then retried.
    /// </param>
    /// <param name="isolationLevel">The isolation level of the transaction.</param>
    public static async Task<TResult> ExecuteReplaySafeAsync<TResult>(
        this DbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<bool>>? verifySucceeded = null,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);

        // The in-memory provider has no transactions and no strategy worth having: the unit tests
        // that run on it get the operation and nothing else.
        if (!context.Database.IsRelational())
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();
        var state = new AttemptState<TResult>();

        return await strategy.ExecuteAsync(
            state: state,
            operation: async (db, s, ct) =>
            {
                // A transaction that is already open belongs to an outer unit of work: this one
                // joins it rather than opening a second one, which the database refuses anyway.
                if (db.Database.CurrentTransaction is not null)
                {
                    return await operation(ct).ConfigureAwait(false);
                }

                await using var transaction = await db.Database
                    .BeginTransactionAsync(isolationLevel, ct)
                    .ConfigureAwait(false);

                if (s.Attempts > 0)
                {
                    // The attempt before this one failed and was rolled back. What the tracker holds
                    // is that attempt's work, not the state the body started from, so the retry
                    // starts by putting the two back in step.
                    await s.Snapshot!.RestoreAsync(db).ConfigureAwait(false);
                }
                else
                {
                    s.Snapshot = TrackerSnapshot.Capture(db);
                }

                s.Attempts++;

                s.CommitFailed = false;
                var value = await operation(ct).ConfigureAwait(false);
                s.Result = value;

                // From here on the failure is the ambiguous one: the commit may have landed and the
                // answer may have been lost on the way back.
                s.CommitFailed = true;
                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return value;
            },
            verifySucceeded: async (db, s, ct) => new ExecutionResult<TResult>(
                s.CommitFailed
                && verifySucceeded is not null
                && await verifySucceeded(ct).ConfigureAwait(false),
                s.Result),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The change tracker as it was before the body of the transaction ran.
    /// </summary>
    /// <remarks>
    /// The body may run twice, and the second run has to start where the first one started: the
    /// values it changed are undone and the state it found them in is restored, so its re-read
    /// sees what it saw the first time and its re-application lands on the same row.
    /// <para>
    /// The entities the body CREATED are not in the snapshot, and they are detached before the
    /// retry: they were rolled back with the transaction, and the body adds them again — so the
    /// second attempt writes one row, not two, and two instances of one key are never tracked at
    /// once.
    /// </para>
    /// </remarks>
    private sealed class TrackerSnapshot
    {
        private readonly List<EntrySnapshot> _entries = new();

        public static TrackerSnapshot Capture(DbContext context)
        {
            var snapshot = new TrackerSnapshot();

            foreach (var entry in context.ChangeTracker.Entries())
            {
                snapshot._entries.Add(new EntrySnapshot(
                    entry.Entity,
                    entry.State,
                    entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.OriginalValues[p.Name]),
                    entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => entry.CurrentValues[p.Name])));
            }

            return snapshot;
        }

        public Task RestoreAsync(DbContext context)
        {
            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (!_entries.Any(e => ReferenceEquals(e.Entity, entry.Entity)))
                {
                    entry.State = EntityState.Detached;
                }
            }

            foreach (var snapshot in _entries)
            {
                var entry = context.Entry(snapshot.Entity);

                foreach (var (name, value) in snapshot.OriginalValues)
                {
                    var property = entry.Property(name);

                    // A value the database generates is the database's to give: it was rolled back
                    // with the transaction, and the insert will ask for a new one.
                    if (property.Metadata.ValueGenerated == ValueGenerated.OnAdd)
                    {
                        continue;
                    }

                    property.OriginalValue = value;
                }

                foreach (var (name, value) in snapshot.CurrentValues)
                {
                    var property = entry.Property(name);

                    if (property.Metadata.ValueGenerated == ValueGenerated.OnAdd)
                    {
                        continue;
                    }

                    property.CurrentValue = value;
                }

                entry.State = snapshot.State;
            }

            return Task.CompletedTask;
        }

        private sealed record EntrySnapshot(
            object Entity,
            EntityState State,
            Dictionary<string, object?> OriginalValues,
            Dictionary<string, object?> CurrentValues);
    }

    /// <summary>
    /// What the transaction knows about itself: how many attempts it has made, whether the failure
    /// it is carrying happened at the commit (the ambiguous one) or before it (an ordinary failure),
    /// what the operation answered, and how the tracker looked when the body started.
    /// </summary>
    private sealed class AttemptState<TResult>
    {
        public int Attempts { get; set; }

        public bool CommitFailed { get; set; }

        public TResult Result { get; set; } = default!;

        public TrackerSnapshot? Snapshot { get; set; }
    }
}
