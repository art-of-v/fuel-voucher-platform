using System.Diagnostics;
using FuelFlow.SharedKernel.Observability;
using Hangfire.Common;
using Hangfire.Server;

namespace FuelFlow.JobsWorker.Observability;

/// <summary>
/// Records success/failure and duration for every Hangfire job.
/// <para>
/// Implemented as a server filter rather than instrumenting each job body so that
/// any recurring job added later is measured automatically and cannot be forgotten.
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
