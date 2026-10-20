using System.Linq.Expressions;
using Hangfire;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// The recurring-job schedule, and the reconciliation that keeps it honest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Hangfire's <c>AddOrUpdate</c> upserts the ids the code knows about. It
/// does NOT remove an id the code has forgotten, so a job that is renamed or deleted leaves its
/// registration behind in <c>hangfire.set['recurring-jobs']</c> forever. Hangfire then tries to
/// load that job's type on every server start, and when the type is gone it says
/// <c>Recurring job 'x' can't be scheduled due to an error and will be disabled</c> - which means
/// the job silently stops running.
/// </para>
/// <para>
/// This is not hypothetical. It happened twice on 2026-10-20: <c>sync-upgwog-pump-prices</c> and
/// <c>sync-klo-prices</c> both failed to load their type, and production's <c>error_logs</c>
/// carried exactly those two rows and nothing else. The state was repaired by the next deploy,
/// which re-registered both ids. The MECHANISM was not repaired, and the next rename does it
/// again.
/// </para>
/// <para>
/// <b>Why the ids are collected here rather than in a list beside the registrations.</b> A second
/// list of ids is a second thing to forget to update, and forgetting it is the same defect this
/// class exists to remove. <see cref="Add"/> registers the job AND records its id, so the two
/// cannot drift.
/// </para>
/// </remarks>
public sealed class RecurringJobSchedule
{
    private readonly IRecurringJobManager _manager;
    private readonly List<string> _registeredJobIds = new();

    public RecurringJobSchedule(IRecurringJobManager manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// The ids this build registers. Every one of them is in Hangfire's registry after
    /// <see cref="Add"/> has run.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredJobIds => _registeredJobIds;

    /// <summary>
    /// Registers a recurring job and records its id.
    /// </summary>
    /// <remarks>
    /// The id is recorded by this method rather than by the caller, so that the set of ids the
    /// code registers and the set the reconciliation compares against are the same set by
    /// construction.
    /// </remarks>
    public void Add<T>(string recurringJobId, Expression<Func<T, Task>> methodCall, string cronExpression)
        where T : class
    {
        _manager.AddOrUpdate(recurringJobId, methodCall, cronExpression);
        _registeredJobIds.Add(recurringJobId);
    }

    /// <summary>
    /// Removes every stored registration the code no longer knows about, and returns the ids it
    /// removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stored ids are read from the raw set rather than from a job descriptor, on purpose: a
    /// descriptor resolves the job's type, and a stale registration's type is exactly the thing
    /// that no longer exists. Reading the set cannot fail on the thing it is looking for.
    /// </para>
    /// <para>
    /// The comparison is ordinal and case-sensitive. A job id is a case-sensitive string, and
    /// treating <c>Sync-Klo-Prices</c> and <c>sync-klo-prices</c> as the same thing would remove a
    /// live registration.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> RemoveStaleRegistrations(IEnumerable<string> storedJobIds)
    {
        var stale = (storedJobIds ?? Enumerable.Empty<string>())
            .Except(_registeredJobIds, StringComparer.Ordinal)
            .ToList();

        foreach (var staleJobId in stale)
        {
            // RemoveIfExists, not Remove: the id may already be gone, and a throw here would take
            // the whole boot down over a registration that is already cleaned up.
            _manager.RemoveIfExists(staleJobId);
        }

        return stale;
    }
}
