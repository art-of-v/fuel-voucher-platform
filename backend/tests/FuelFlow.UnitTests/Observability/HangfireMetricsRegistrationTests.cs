using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FuelFlow.UnitTests.Observability;

/// <summary>
/// Guards that the Hangfire job metrics are actually attached in the host that production runs.
/// <para>
/// <c>MetricNameContractTests</c> proves the instrument exists and that alert rules name a real
/// metric. Neither can see that the <see cref="MetricsJobFilter"/> was attached at all: the
/// filter lived in JobsWorker, which production does not deploy, so in the one process that
/// actually runs the jobs nothing incremented the counter and the HangfireJobFailures alert
/// matched an empty series. A missing global filter is invisible to a name-level assertion.
/// </para>
/// </summary>
public class HangfireMetricsRegistrationTests
{
    /// <summary>
    /// <c>GlobalJobFilters</c> is process-wide static state shared by every test in this
    /// assembly, so each test restores it.
    /// </summary>
    private static void ResetGlobalFilters() => GlobalJobFilters.Filters.Clear();

    /// <summary>
    /// Hangfire wraps an attribute-based filter in a <see cref="JobFilter"/> that keeps the
    /// attribute on <see cref="JobFilter.Instance"/>, so that is what the collection is searched
    /// by - both here and in the registration helper.
    /// </summary>
    private static int CountRegistered() =>
        GlobalJobFilters.Filters.Count(filter => filter.Instance is MetricsJobFilter);

    private sealed class ServiceProviderHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public void Dispose() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static IHost BuildHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<FuelFlowMetrics>();
        services.AddSingleton<MetricsJobFilter>();
        return new ServiceProviderHost(services.BuildServiceProvider());
    }

    [Fact]
    public void Registers_the_filter_so_the_job_counters_are_emitted()
    {
        ResetGlobalFilters();
        try
        {
            BuildHost().RegisterHangfireMetricsJobFilter();

            CountRegistered().Should().Be(1);
        }
        finally
        {
            ResetGlobalFilters();
        }
    }

    [Fact]
    public void Is_idempotent_because_the_filter_collection_is_process_wide_static()
    {
        ResetGlobalFilters();
        try
        {
            var host = BuildHost();

            host.RegisterHangfireMetricsJobFilter();
            host.RegisterHangfireMetricsJobFilter();
            host.RegisterHangfireMetricsJobFilter();

            // Registering twice would count every job twice: the alert would fire on the first
            // failure of a single job and the dashboard would overstate the volume.
            CountRegistered().Should().Be(1);
        }
        finally
        {
            ResetGlobalFilters();
        }
    }

    [Fact]
    public void Leaves_filters_registered_by_something_else_alone()
    {
        ResetGlobalFilters();
        try
        {
            var host = BuildHost();
            var other = new OtherFilter();
            GlobalJobFilters.Filters.Add(other);

            host.RegisterHangfireMetricsJobFilter();

            GlobalJobFilters.Filters.Count(f => f.Instance is OtherFilter).Should().Be(1);
            CountRegistered().Should().Be(1);
        }
        finally
        {
            ResetGlobalFilters();
        }
    }

    private sealed class OtherFilter : JobFilterAttribute, IServerFilter
    {
        public void OnPerforming(PerformingContext context) { }
        public void OnPerformed(PerformedContext context) { }
    }
}