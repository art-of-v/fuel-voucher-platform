using System.Linq.Expressions;
using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// Asserts that the API's own host attaches the Hangfire job metrics filter.
/// <para>
/// <c>HangfireMetricsRegistrationTests</c> (unit) covers the registration helper; it cannot see
/// whether <c>Program.cs</c> calls it. That distinction is the whole bug: the helper's logic was
/// never in question - the call was simply missing from the only host that runs jobs in
/// production, so <c>fuelflow_jobs_failed_jobs_total</c> was never emitted and the
/// HangfireJobFailures alert watched an empty series, which reads exactly like a healthy system.
/// </para>
/// <para>
/// This boots the real composition root through the shared fixture, so deleting the
/// <c>RegisterHangfireMetricsJobFilter()</c> line from <c>Program.cs</c> fails here.
/// </para>
/// </summary>
[Collection("Integration Tests")]
public class HangfireMetricsWiringTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public HangfireMetricsWiringTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Api_host_attaches_the_hangfire_job_metrics_filter()
    {
        // CreateClient forces the host - and therefore Program.cs top to bottom - to build.
        _ = _fixture.CreateClient();

        // Hangfire wraps an attribute-based filter in a JobFilter that keeps the attribute on
        // JobFilter.Instance, so the collection never holds a MetricsJobFilter directly.
        var attached = GlobalJobFilters.Filters
            .Select(filter => filter.Instance)
            .OfType<MetricsJobFilter>()
            .ToList();

        attached.Should().NotBeEmpty(
            "production runs Hangfire in-process in this API and scrapes only this process, so a " +
            "filter attached elsewhere leaves fuelflow_jobs_succeeded/failed never emitted and the " +
            "HangfireJobFailures alert permanently silent");
    }

    [Fact]
    public void Api_host_resolves_the_filter_from_its_own_container()
    {
        _ = _fixture.CreateClient();

        // Registered before the container is built. A filter added to builder.Services after
        // Build() would resolve to nothing at all and the failure would be silent.
        _fixture.Services.GetService<MetricsJobFilter>().Should().NotBeNull();
    }
}