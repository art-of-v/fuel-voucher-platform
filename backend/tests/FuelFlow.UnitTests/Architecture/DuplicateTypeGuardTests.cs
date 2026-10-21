using System.Reflection;
using FluentAssertions;

namespace FuelFlow.UnitTests.Architecture;

/// <summary>
/// One home per type: the standalone jobs worker does not declare a type the API also declares.
/// </summary>
/// <remarks>
/// <para>
/// The concrete failure this exists for: <c>FuelFlow.JobsWorker</c> carried three copies of types
/// that live in the API - <c>OrderCreatedPayload</c>, <c>OrderFulfilledPayload</c> and
/// <c>NotificationService</c> - and the notification service had <b>diverged</b>. The API's writes
/// the in-app row and sends the push; the worker's wrote the in-app row only, had no
/// safety-net claim window, and had no voucher-issued path at all. Two implementations of one
/// job, one of them a year behind, and nothing in the build said so.
/// </para>
/// <para>
/// The other two copies were identical, which is worse in its way: a reader comparing the two
/// files would find them the same and conclude that the worker's copy was fine.
/// </para>
/// <para>
/// The worker is not deployed - production runs Hangfire in-process in the API - but it is built
/// by CI, it is run locally, and it schedules the same jobs against the same storage. A copy
/// that drifts in a process nobody deploys is still a copy that somebody reads.
/// </para>
/// <para>
/// The rule is the one the codebase already follows for the other background jobs: the API is the
/// home, and the worker refers to it (an alias, the way <c>FulfillmentService</c>,
/// <c>RefundStatusSyncService</c> and <c>VoucherStockMonitor</c> are referred to). This test is
/// what keeps that true when the next type is added.
/// </para>
/// </remarks>
public sealed class DuplicateTypeGuardTests
{
    [Fact]
    public void No_Type_Should_Be_Declared_In_Both_The_Api_And_The_JobsWorker()
    {
        var api = typeof(FuelFlow.API.BackgroundJobs.NotificationService).Assembly;

        var worker = LoadWorkerAssembly();

        var apiTypeNames = api
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var duplicates = worker
            .GetExportedTypes()
            // The entry point is not a declared type: the compiler names it after the file's
            // top-level statements, and every executable has one. Both assemblies have one.
            .Where(type => type != worker.EntryPoint?.DeclaringType)
            .Where(type => apiTypeNames.Contains(type.Name))
            .Select(type => $"{type.FullName} (the API also declares {type.Name})")
            .OrderBy(dup => dup, StringComparer.Ordinal)
            .ToArray();

        duplicates.Should().BeEmpty(
            "a type declared in two assemblies is two implementations of one thing, and only one of "
            + "them is the one that runs. The API is the home of the background jobs - the worker "
            + "refers to them through an alias, the way FulfillmentService and "
            + "RefundStatusSyncService are - so that the two processes cannot drift apart. This "
            + "guard exists because NotificationService did exactly that: the worker's copy had "
            + "no push sender, no safety-net claim window and no voucher-issued path");
    }

    /// <summary>
    /// The worker's assembly, loaded by name. It is not referenced by a type here because its
    /// entry point is a top-level statement, and the class the compiler generates for it is
    /// internal.
    /// </summary>
    /// <remarks>
    /// A worker that cannot be loaded is a failure, not a pass: it means the project reference
    /// that carries it into this test project is gone, and this guard would then be guarding
    /// nothing while still reporting green.
    /// </remarks>
    private static Assembly LoadWorkerAssembly()
    {
        try
        {
            return Assembly.Load("FuelFlow.JobsWorker");
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException(
                "The FuelFlow.JobsWorker assembly could not be loaded, so this guard cannot see "
                + "what the worker declares. The project reference to it in "
                + "FuelFlow.UnitTests.csproj is what puts it on the test output path - without "
                + "it, this test would pass without checking anything.",
                ex);
        }
    }
}
