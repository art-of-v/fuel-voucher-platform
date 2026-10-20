using FluentAssertions;
using FuelFlow.SharedKernel.Observability;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FuelFlow.IntegrationTests;

/// <summary>
/// The recurring-job registry has to be reconciled, because <c>AddOrUpdate</c> upserts the ids the
/// code knows about and does not remove an id the code has forgotten.
/// </summary>
/// <remarks>
/// <para>
/// A job that was renamed or deleted leaves its registration behind in
/// <c>hangfire.set['recurring-jobs']</c> forever. Hangfire then tries to load that job's type on
/// every server start, and when the type is gone it disables the job and says so in a line of
/// <c>error_logs</c> that nobody reads. It happened twice on 2026-10-20.
/// </para>
/// <para>
/// <b>Why this suite uses a real Hangfire storage.</b> The reconciliation reads the stored ids from
/// the raw set, and the name of that set is the thing that could be wrong - a wrong name and the
/// reconciliation finds nothing and does nothing, silently. Only a real storage answers that.
/// </para>
/// </remarks>
[Collection("Integration Tests")]
public sealed class RecurringJobScheduleIntegrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public RecurringJobScheduleIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void A_Job_The_Code_No_Longer_Registers_Is_Removed_From_The_Registry()
    {
        // Constructing the storage is what installs the schema, the same way a production boot does.
        var storage = new PostgreSqlStorage(_fixture.ConnectionString);
        var manager = new RecurringJobManager(storage);

        // The registry is shared with the other tests in this class - and with whatever a previous
        // test left behind - so it is emptied first. The reconciliation removes EVERY id the code
        // does not register, so a leftover from another test would otherwise be counted here.
        ClearRegistry(storage, manager);

        // Two registrations, as if a previous build had registered both: one this build still
        // registers, and one it has forgotten.
        manager.AddOrUpdate<ProbeJob>(
            "live-job",
            service => service.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        manager.AddOrUpdate<ProbeJob>(
            "stale-job",
            service => service.RunAsync(CancellationToken.None),
            "*/5 * * * *");

        // This build registers only one of them.
        var schedule = new RecurringJobSchedule(manager);
        schedule.Add<ProbeJob>(
            "live-job",
            service => service.RunAsync(CancellationToken.None),
            "*/5 * * * *");

        var storedJobIds = storage.GetConnection().GetAllItemsFromSet("recurring-jobs");

        var staleJobIds = schedule.RemoveStaleRegistrations(storedJobIds);

        staleJobIds.Should().ContainSingle("exactly one registration is a leftover");
        staleJobIds[0].Should().Be("stale-job");

        var remaining = storage.GetConnection().GetAllItemsFromSet("recurring-jobs");
        remaining.Should().NotContain("stale-job", "the leftover is what Hangfire cannot load");
        remaining.Should().Contain("live-job", "the job this build registers keeps running");
    }

    [Fact]
    public void The_Registry_The_Reconciliation_Reads_Is_The_One_Hangfire_Writes()
    {
        var storage = new PostgreSqlStorage(_fixture.ConnectionString);
        var manager = new RecurringJobManager(storage);

        ClearRegistry(storage, manager);

        manager.AddOrUpdate<ProbeJob>(
            "set-membership-probe",
            service => service.RunAsync(CancellationToken.None),
            "*/5 * * * *");

        // The set name is the whole mechanism: a wrong name and the reconciliation reads an empty
        // set every time and removes nothing, without a word.
        storage.GetConnection()
            .GetAllItemsFromSet("recurring-jobs")
            .Should().Contain("set-membership-probe");
    }

    /// <summary>
    /// Empties the recurring-job registry, through the public API.
    /// </summary>
    private static void ClearRegistry(JobStorage storage, IRecurringJobManager manager)
    {
        foreach (var jobId in storage.GetConnection().GetAllItemsFromSet("recurring-jobs").ToList())
        {
            manager.RemoveIfExists(jobId);
        }
    }

    [Fact]
    public void Hangfire_Registers_The_Storage_The_Reconciliation_Reads()
    {
        // The reconciliation resolves JobStorage from the container. If Hangfire did not register it,
        // the API would fail to BOOT - and a boot failure is a much worse way to learn that than a
        // test is.
        var services = new ServiceCollection();
        services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(_fixture.ConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetService<JobStorage>().Should().NotBeNull(
            "the reconciliation resolves JobStorage from the container");
        provider.GetService<IRecurringJobManager>().Should().NotBeNull();
    }

    /// <summary>
    /// A job type for the registry to point at. It has to exist and be loadable - that is exactly
    /// what a stale registration fails to be.
    /// </summary>
    public sealed class ProbeJob
    {
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
