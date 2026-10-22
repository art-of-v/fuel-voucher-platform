using FluentAssertions;
using FuelFlow.BackgroundJobs;
using FuelFlow.Features.Admin.SyncCatalogue;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace FuelFlow.UnitTests.Admin;

/// <summary>
/// The on-demand catalogue check: the three jobs the weekly schedule runs, started by hand.
/// </summary>
/// <remarks>
/// The point of the endpoint is that an operator does not have to wait for the week to come round -
/// after a catalogue is emptied, or when a vendor is known to have added a grade, the check runs now.
/// So the thing worth asserting is that it enqueues the CATALOGUE half of all three, and that it
/// enqueues rather than calls: each job fetches a third party's page, and a request that waited on
/// three of those would hold a connection open for as long as the slowest vendor takes to answer.
/// </remarks>
public sealed class AdminSyncCatalogueControllerTests
{
    [Fact]
    public void It_should_enqueue_the_catalogue_check_for_every_vendor()
    {
        var jobs = new Mock<IBackgroundJobClient>();
        var controller = new AdminSyncCatalogueController(jobs.Object);

        var result = controller.Sync();

        result.Should().BeOfType<AcceptedResult>();

        // The invocations are inspected rather than the calls verified: Hangfire's Enqueue<T> takes a
        // job EXPRESSION, and naming that type in a test would mean a test about Hangfire rather than
        // about this endpoint. The service is read off the job's own Type.
        var enqueued = jobs.Invocations
            .Where(i => i.Arguments.Count > 0 && i.Arguments[0] is Hangfire.Common.Job job)
            .Select(i => ((Hangfire.Common.Job)i.Arguments[0]!).Type)
            .ToList();

        enqueued.Should().BeEquivalentTo(
            [typeof(OkkoPriceSyncService), typeof(UpgWogPriceSyncService), typeof(KloPriceSyncService)]);
    }
}
