using System.Diagnostics;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FuelFlow.SharedKernel.Observability;

/// <summary>
/// Records success/failure and duration for every Hangfire job.
/// <para>
/// Implemented as a server filter rather than instrumenting each job body so that
/// any recurring job added later is measured automatically and cannot be forgotten.
/// </para>
/// <para>
/// Lives in the shared kernel rather than in JobsWorker because production runs Hangfire
/// in-process in the API and deploys no JobsWorker container at all. Registered only in the
/// worker, the filter was never attached in the one host that actually runs the jobs, so
/// <c>fuelflow_jobs_succeeded</c> / <c>fuelflow_jobs_failed</c> were never emitted in production
/// and the HangfireJobFailures alert matched an empty series forever - which looks exactly
/// like "everything is healthy". Same class of mistake as the business gauges, fixed the
/// same way: one registration helper both hosts call, so it cannot drift again.
/// </para>
/// <para>
/// Each finished run also writes one log line, because the counters on their own leave a failure
/// unexplained: they name the job and the exception type and stop there - no message, no stack
/// trace, and nothing to read after Hangfire's automatic retries have already cleared the failed
/// state in its dashboard. The counters also cannot carry a one-off: a daily job that fails once
/// is a single increment, which the rate-based <c>HangfireJobFailures</c> rule can miss, while the
/// log line stays searchable for the whole retention window. The level follows the outcome - Error
/// with the exception attached, Warning for a cancellation (a deploy stopping the server is not a
/// defect and must not page anyone), Information for a completed run - so every run is traceable
/// at the default minimum level.
/// </para>
/// </summary>
public sealed class MetricsJobFilter : JobFilterAttribute, IServerFilter
{
    private const string StopwatchKey = "FuelFlowMetricsStopwatch";

    private readonly FuelFlowMetrics _metrics;
    private readonly ILogger<MetricsJobFilter> _logger;

    public MetricsJobFilter(FuelFlowMetrics metrics, ILogger<MetricsJobFilter> logger)
    {
        _metrics = metrics;
        _logger = logger;
    }

    public void OnPerforming(PerformingContext context)
    {
        context.Items[StopwatchKey] = Stopwatch.StartNew();
    }

    public void OnPerformed(PerformedContext context)
    {
        var job = context.BackgroundJob.Job;
        var jobName = job.Method.Name;
        var jobType = job.Type.Name;

        var elapsedMs = context.Items.TryGetValue(StopwatchKey, out var value) && value is Stopwatch sw
            ? sw.Elapsed.TotalMilliseconds
            : 0;

        // Cancellation is checked before the exception on purpose: Hangfire marks a stopped run as
        // canceled and may still surface the OperationCanceledException behind it, so reading the
        // exception first would report a deploy that stopped the server as a job failure.
        if (context.Canceled)
        {
            // Deliberately neither a success nor a failure: the job did not finish its work, and
            // counting it as a success would hide a canceled run inside a rising success rate.
            _logger.LogWarning(
                "Background job {JobType}.{JobName} was cancelled after {DurationMs:0}ms",
                jobType, jobName, elapsedMs);
            return;
        }

        if (context.Exception is not null)
        {
            _metrics.JobFailed(jobName, context.Exception.GetType().Name);
            _logger.LogError(
                context.Exception,
                "Background job {JobType}.{JobName} failed after {DurationMs:0}ms",
                jobType, jobName, elapsedMs);
            return;
        }

        _metrics.JobSucceeded(jobName, elapsedMs);
        _logger.LogInformation(
            "Background job {JobType}.{JobName} completed in {DurationMs:0}ms",
            jobType, jobName, elapsedMs);
    }
}

/// <summary>
/// Attaches <see cref="MetricsJobFilter"/> to Hangfire's global filters.
/// <para>
/// Idempotent on purpose. <c>GlobalJobFilters</c> is process-wide static state, so a second
/// call would attach the filter twice and every job would be counted twice - the alert would
/// then fire on the first failure of a single job and the dashboard would overstate the
/// volume. Both hosts call this at startup; neither should have to know whether the other one
/// already did.
/// </para>
/// </summary>
public static class HangfireMetricsSetup
{
    public static IHost RegisterHangfireMetricsJobFilter(this IHost host)
    {
        var filters = GlobalJobFilters.Filters;

        // Hangfire stores a JobFilterAttribute by wrapping it in a JobFilter, so the collection
        // never contains a MetricsJobFilter directly - the attribute hangs off JobFilter.Instance.
        // An OfType<MetricsJobFilter> guard here would never match and would double-register.
        if (filters.Any(filter => filter.Instance is MetricsJobFilter))
            return host;

        filters.Add(host.Services.GetRequiredService<MetricsJobFilter>());
        return host;
    }
}