using System.Diagnostics.Metrics;
using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Moq;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards the trail a finished Hangfire run leaves behind.
/// <para>
/// The counters answered "did a job fail?" and nothing else: a failed run left one increment and,
/// once Hangfire's retries cleared the failed state, nothing a human could read. Production runs
/// every job inside the API process, so those lines also land in the same stream as the framework
/// chatter and need their own searchable shape - hence the message prefix, the job type and name
/// fields, and the level per outcome that the Loki alert rule matches.
/// </para>
/// </summary>
public class MetricsJobFilterTests
{
    private static readonly BackgroundJob RegisteredJob = new(
        "job-1",
        Job.FromExpression(() => SampleJob.RunAsync()),
        DateTime.UnixEpoch);

    private sealed class SampleJob
    {
        public static Task RunAsync() => Task.CompletedTask;
    }

    /// <summary>
    /// Hangfire hands the same <see cref="PerformContext"/> to the performing and performed hooks;
    /// that shared instance is what carries the stopwatch between them.
    /// </summary>
    private static (PerformingContext Performing, PerformedContext Performed) Contexts(
        Exception? exception = null, bool canceled = false)
    {
        var connection = new Mock<IStorageConnection>(MockBehavior.Loose).Object;

        // The filter only reads job metadata and its own Items, so a real JobStorage is not needed;
        // constructing one would also require a configured global storage.
        var perform = new PerformContext(
            null!, connection, RegisteredJob, new JobCancellationToken(canceled: false));

        return (
            new PerformingContext(perform),
            new PerformedContext(perform, result: null, canceled, exception));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> _measurements = [];

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ObservabilityConstants.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };

            // The meter name is process-wide, so a listener that records every instrument also hears
            // the business counters other test classes emit while this one runs in parallel - which
            // is how a "jobs.failed" assertion once saw an order metric. This test is about the job
            // counters, so it captures only those.
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                if (instrument.Name.StartsWith("fuelflow.jobs.", StringComparison.Ordinal))
                    _measurements.Add((instrument.Name, value, tags.ToArray()));
            });

            _listener.Start();
        }

        public IReadOnlyList<(string Name, long Value, KeyValuePair<string, object?>[] Tags)> Measurements =>
            _measurements;

        public void Dispose() => _listener.Dispose();
    }

    private sealed class Harness : IDisposable
    {
        private readonly FuelFlowMetrics _metrics = new();

        public RecordingLogger<MetricsJobFilter> Logger { get; } = new();

        public MetricCapture Metrics { get; } = new();

        public MetricsJobFilter Filter => new(_metrics, Logger);

        public void Dispose()
        {
            Metrics.Dispose();
            _metrics.Dispose();
        }
    }

    [Fact]
    public void Failure_LogsTheException_AtError_WithTheJobAndItsDuration()
    {
        using var harness = new Harness();
        var exception = new InvalidOperationException("the provider refused the batch");
        var (performing, performed) = Contexts(exception: exception);
        var filter = harness.Filter;

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        var line = harness.Logger.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Error);
        line.Exception.Should().BeSameAs(exception, "the stack trace is the only way to explain the failure after the fact");
        line.Message.Should().StartWith("Background job SampleJob.RunAsync failed after");
    }

    [Fact]
    public void Failure_StillIncrementsTheFailedCounter()
    {
        using var harness = new Harness();
        var (performing, performed) = Contexts(exception: new TimeoutException());
        var filter = harness.Filter;

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        harness.Metrics.Measurements.Select(m => m.Name).Should().Equal("fuelflow.jobs.failed");
        harness.Metrics.Measurements.Single().Tags.Should().Contain(
            new KeyValuePair<string, object?>("job_name", "RunAsync"));
    }

    [Fact]
    public void Success_LogsCompletion_AtInformation_AndCountsASuccess()
    {
        using var harness = new Harness();
        var (performing, performed) = Contexts();
        var filter = harness.Filter;

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        var line = harness.Logger.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Information);
        line.Exception.Should().BeNull();
        line.Message.Should().StartWith("Background job SampleJob.RunAsync completed in");

        harness.Metrics.Measurements.Select(m => m.Name).Should().Equal("fuelflow.jobs.succeeded");
    }

    [Fact]
    public void Cancellation_LogsWarning_AndCountsNeitherSuccessNorFailure()
    {
        using var harness = new Harness();
        var (performing, performed) = Contexts(canceled: true);
        var filter = harness.Filter;

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        var line = harness.Logger.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Message.Should().StartWith("Background job SampleJob.RunAsync was cancelled after");

        // A cancelled run neither finished its work nor broke - counting it as a success would hide
        // it inside a rising success rate, counting it as a failure would blame a deploy.
        harness.Metrics.Measurements.Should().BeEmpty();
    }

    [Fact]
    public void Cancellation_IsNotReportedAsAFailure_EvenWhenTheExceptionArrivesWithIt()
    {
        using var harness = new Harness();
        var (performing, performed) = Contexts(exception: new OperationCanceledException(), canceled: true);
        var filter = harness.Filter;

        filter.OnPerforming(performing);
        filter.OnPerformed(performed);

        harness.Logger.Entries.Single().Level.Should().Be(LogLevel.Warning);
        harness.Metrics.Measurements.Should().NotContain(
            m => m.Name == "fuelflow.jobs.failed",
            "stopping the server is a deploy, not a defect worth waking someone for");
    }
}
