using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FuelFlow.UnitTests.Persistence;

/// <summary>
/// Architecture guard: every explicit EF transaction in the app runs inside the execution strategy.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's retrying execution strategy is enabled on the Npgsql provider (DatabaseSetup). A
/// strategy refuses a user-initiated transaction: with <c>EnableRetryOnFailure</c> configured, the
/// first save inside a transaction opened by hand throws
/// <c>InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy'
/// does not support user-initiated transactions</c> — at the save, not at the BeginTransaction, so it
/// is a runtime failure on the money paths rather than a compile error.
/// </para>
/// <para>
/// The throw is what makes this guard worth having: the transaction has to be opened through
/// <c>ReplaySafeTransaction</c>, which opens it inside the strategy and makes the write safe to
/// replay. A transaction opened anywhere else fails at runtime, in production, on the day it is
/// written.
/// </para>
/// <para>
/// The one exception is the raw Npgsql connection in
/// <c>HangfireHistoryCleanupService</c>: it is not EF, it does not go through the strategy, and it
/// touches Hangfire's own tables.
/// </para>
/// </remarks>
public sealed class TransactionStrategyGuardTests
{
    /// <summary>The one file allowed to open a transaction: the helper every other site goes through.</summary>
    private const string HelperPath = "SharedKernel/Database/ReplaySafeTransaction.cs";

    /// <summary>The raw connection that is not EF's, and so not the strategy's business.</summary>
    private const string RawHangfireConnectionPath = "BackgroundJobs/HangfireHistoryCleanupService.cs";

    [Fact]
    public void EveryExplicitTransaction_IsOpenedInsideTheExecutionStrategy()
    {
        var apiRoot = LocateApiProjectRoot();

        var offenders = Directory
            .EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsInScope)
            .Select(path => (path, text: StripComments(File.ReadAllText(path))))
            .Where(f => OpensATransaction(f.text))
            .Select(f => Path.GetRelativePath(apiRoot, f.path).Replace('\\', '/'))
            .Where(path => path != HelperPath && path != RawHangfireConnectionPath)
            .OrderBy(p => p)
            .ToList();

        offenders.Should().BeEmpty(
            "the retrying execution strategy refuses a user-initiated transaction - the first save " +
            "inside one throws at runtime, on the money paths. Open the transaction through " +
            "ReplaySafeTransaction instead: it opens the transaction inside the strategy and makes " +
            "the write safe to replay. Planning issue #610.");
    }

    /// <summary>
    /// A transaction opened by hand. <c>BeginTransaction</c> on the database facade is EF's; the
    /// connection's own <c>BeginTransaction</c> is not, and is not the strategy's business.
    /// </summary>
    private static bool OpensATransaction(string text)
        => text.Contains(".Database.BeginTransactionAsync")
        || text.Contains(".Database.BeginTransaction(");

    /// <summary>
    /// Removes // and /* */ comments before scanning: the prose in this file and its siblings talks
    /// about transactions, and a comment that mentions one is not a call site.
    /// </summary>
    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", " ");
    }

    private static bool IsInScope(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/bin/") || normalized.Contains("/obj/")) return false;
        return true;
    }

    /// <summary>
    /// Resolves backend/src/FuelFlow.API from this test file's compile-time location, with a
    /// fallback that walks up from the test assembly's directory.
    /// </summary>
    private static string LocateApiProjectRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = .../backend/tests/FuelFlow.UnitTests/Persistence/<this>.cs
        // Up 3: FuelFlow.UnitTests -> tests -> backend
        var backend = Directory.GetParent(thisFile)!.Parent!.Parent!.Parent!.FullName;
        var candidate = Path.Combine(backend, "src", "FuelFlow.API");
        if (Directory.Exists(candidate)) return candidate;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var probe = Path.Combine(dir.FullName, "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            probe = Path.Combine(dir.FullName, "backend", "src", "FuelFlow.API");
            if (Directory.Exists(probe)) return probe;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate backend/src/FuelFlow.API from either the test file path or the assembly directory.");
    }
}
