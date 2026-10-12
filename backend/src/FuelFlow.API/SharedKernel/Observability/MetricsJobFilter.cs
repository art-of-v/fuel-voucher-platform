using System.Diagnostics;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
/// </summary>
public sealed class MetricsJobFilter : JobFilterAttribute, IServerFilter
{
    private const string StopwatchKey = "FuelFlowMetricsStopwatch";

    private readonly FuelFlowMetrics _metrics;

    public MetricsJobFilter(FuelFlowMetrics metrics) => _metrics = metrics;

    public void OnPerforming(PerformingContext context)
    {
        context.Items[StopwatchKey] = Stopwatch.StartNew();
    }

    public void OnPerformed(PerformedContext context)
    {
        var jobName = context.BackgroundJob.Job.Method.Name;

        if (context.Exception is not null)
        {
            _metrics.JobFailed(jobName, context.Exception.GetType().Name);
            return;
        }

        var elapsedMs = context.Items.TryGetValue(StopwatchKey, out var value) && value is Stopwatch sw
            ? sw.Elapsed.TotalMilliseconds
            : 0;

        _metrics.JobSucceeded(jobName, elapsedMs);
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