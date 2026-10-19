using FuelFlow.Features.Orders.SharedModels;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FuelFlow.API.BackgroundJobs;

/// <summary>
/// Turns a written outbox row into a queued job, so that dispatching an event does not have to
/// wait for a clock to notice it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this replaces.</b> Three recurring jobs polled <c>outbox_events</c> for unprocessed rows,
/// once a minute each. On a six-user system that is 4,320 wake-ups a day to look at an empty
/// table, and 4,320 rows a day of Hangfire history to say so. It is also a latency the customer
/// pays for: a purchase writes an <c>OrderCreated</c> event, the poller picks it up within a
/// minute, that writes an <c>OrderFulfilled</c> event, and the next poller picks THAT up within
/// another minute - so the voucher notification can be up to two minutes behind the purchase.
/// </para>
/// <para>
/// <b>Why an interceptor and not a helper.</b> The outbox is written in six places, by three
/// different services, and a helper would have to be remembered at each of them. A miss would
/// strand the event until the safety net, which is exactly the failure the outbox exists to
/// prevent. The change tracker sees every one of them.
/// </para>
/// <para>
/// <b>Why the job is scheduled rather than queued.</b> EF Core 10 has no transaction-committed
/// event to hang this on - <c>SaveChangesInterceptor</c> offers SavingChanges, SavedChanges and the
/// failure pair, and the transaction events are gone. SavedChanges fires after EF's own
/// transaction has committed, which is the case in hand; but a caller who opens an explicit
/// transaction has not committed yet at that point, and a job that started now would find no row.
/// The small delay covers that: by the time the job runs, whatever transaction wrote the row has
/// committed.
/// </para>
/// <para>
/// <b>Why it never throws.</b> The outbox row IS the guarantee; the job is only the fast path. A
/// Hangfire storage error must not turn a committed purchase into a failed request, and it must
/// not lose the event either - so the failure is logged and the safety net takes it. That is also
/// why the recurring jobs still exist.
/// </para>
/// </remarks>
public sealed class OutboxDispatchInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// How long before the job looks for the row. Long enough for a caller-managed transaction to
    /// have committed, short enough that nobody measures it.
    /// </summary>
    private static readonly TimeSpan DispatchDelay = TimeSpan.FromSeconds(5);

    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger<OutboxDispatchInterceptor> _logger;
    private readonly List<OutboxEvent> _pending = new();

    /// <summary>
    /// The job client is optional. A host that has no Hangfire - a test that builds a context, the
    /// standalone jobs worker - still writes outbox rows, and the safety net is what dispatches
    /// them there. Making the client required would mean every host that builds a context has to
    /// have Hangfire, which is a coupling the outbox does not need.
    /// </summary>
    public OutboxDispatchInterceptor(
        IBackgroundJobClient? jobs,
        ILogger<OutboxDispatchInterceptor> logger)
    {
        _jobs = jobs;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var added = eventData.Context?.ChangeTracker
            .Entries<OutboxEvent>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (added is { Count: > 0 })
            _pending.AddRange(added);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await base.SavedChangesAsync(eventData, result, cancellationToken);

        if (_pending.Count == 0)
            return result;

        var events = _pending.ToList();
        _pending.Clear();

        if (_jobs is null)
        {
            // No Hangfire in this host. The rows are committed and unprocessed, and the safety net
            // in a host that does have Hangfire will take them.
            _logger.LogDebug(
                "No job client in this host; {Count} outbox event(s) left for the safety net",
                events.Count);
            return result;
        }

        foreach (var outboxEvent in events)
        {
            try
            {
                // The event's own id, not a payload: the job re-reads the row, so a duplicate
                // enqueue is harmless - it sees Processed and does nothing.
                _jobs.Schedule<OutboxDispatchJob>(
                    job => job.DispatchAsync(outboxEvent.Id, CancellationToken.None),
                    DispatchDelay);
            }
            catch (Exception ex)
            {
                // Deliberately swallowed. The row is committed and unprocessed, so the safety net
                // will pick it up; failing here would report a committed purchase as a failed
                // request, which is a worse lie than a delayed notification.
                _logger.LogError(
                    ex,
                    "Could not enqueue the dispatch job for outbox event {EventId}. It stays "
                    + "unprocessed and the safety net will take it.",
                    outboxEvent.Id);
            }
        }

        return result;
    }
}